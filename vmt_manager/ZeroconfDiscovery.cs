/*
MIT License

Copyright (c) 2020 gpsnmeajp

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace vmt_manager
{
    internal sealed class DiscoveryPeer
    {
        public string InstanceId { get; set; }
        public string InstanceName { get; set; }
        public IPAddress Address { get; set; }
        public int OscRecvPort { get; set; }
        public string Capabilities { get; set; }
        public string PairingToken { get; set; }
        public DateTime LastSeenUtc { get; set; }

        public DiscoveryPeer Clone()
        {
            return new DiscoveryPeer
            {
                InstanceId = InstanceId,
                InstanceName = InstanceName,
                Address = Address,
                OscRecvPort = OscRecvPort,
                Capabilities = Capabilities,
                PairingToken = PairingToken,
                LastSeenUtc = LastSeenUtc,
            };
        }
    }

    internal sealed class DiscoverySnapshot
    {
        public DiscoverySnapshot(
            IList<DiscoveryPeer> peers,
            DiscoveryPeer selectedPeer,
            DateTime nowUtc,
            TimeSpan peerTimeout,
            string pinnedPeerId,
            string pairingToken)
        {
            Peers = peers;
            SelectedPeer = selectedPeer;
            NowUtc = nowUtc;
            PeerTimeout = peerTimeout;
            PinnedPeerId = pinnedPeerId;
            PairingToken = pairingToken;
        }

        public IList<DiscoveryPeer> Peers { get; private set; }
        public DiscoveryPeer SelectedPeer { get; private set; }
        public DateTime NowUtc { get; private set; }
        public TimeSpan PeerTimeout { get; private set; }
        public string PinnedPeerId { get; private set; }
        public string PairingToken { get; private set; }
    }

    internal sealed class ZeroconfDiscovery : IDisposable
    {
        public const string MulticastGroup = "239.255.42.99";
        public const int DiscoveryPort = 39580;
        public const int VmtOscRecvPort = 39570;
        public static readonly TimeSpan DefaultPeerTimeout = TimeSpan.FromSeconds(5);

        private readonly object gate = new object();
        private readonly Dictionary<string, DiscoveryPeer> peers = new Dictionary<string, DiscoveryPeer>();
        private readonly string instanceId;
        private readonly string instanceName;
        private readonly TimeSpan peerTimeout;

        private Socket socket;
        private Thread receiveThread;
        private Thread announceThread;
        private bool stopping;
        private string pairingToken;
        private string pinnedPeerId;
        private string selectedEndpointKey;

        public event Action<DiscoverySnapshot> SnapshotChanged;
        public event Action<DiscoveryPeer> SelectedPeerChanged;

        public ZeroconfDiscovery(string instanceId, string instanceName, string pairingToken, string pinnedPeerId)
            : this(instanceId, instanceName, pairingToken, pinnedPeerId, DefaultPeerTimeout)
        {
        }

        public ZeroconfDiscovery(
            string instanceId,
            string instanceName,
            string pairingToken,
            string pinnedPeerId,
            TimeSpan peerTimeout)
        {
            this.instanceId = instanceId;
            this.instanceName = instanceName;
            this.pairingToken = pairingToken ?? "";
            this.pinnedPeerId = pinnedPeerId ?? "";
            this.peerTimeout = peerTimeout;
        }

        public bool HasSelectedPeer
        {
            get
            {
                lock (gate)
                {
                    return !string.IsNullOrEmpty(selectedEndpointKey);
                }
            }
        }

        public void Start()
        {
            if (socket != null)
            {
                return;
            }

            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
            socket.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

            try
            {
                socket.SetSocketOption(
                    SocketOptionLevel.IP,
                    SocketOptionName.AddMembership,
                    new MulticastOption(IPAddress.Parse(MulticastGroup), IPAddress.Any));
            }
            catch (Exception ex)
            {
                Console.WriteLine("# ZeroconfDiscovery: multicast join failed, continuing with broadcast: " + ex.Message);
            }

            receiveThread = new Thread(ReceiveLoop);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            announceThread = new Thread(AnnounceLoop);
            announceThread.IsBackground = true;
            announceThread.Start();

            PublishSnapshot(false);
        }

        public void SetPairingToken(string token)
        {
            lock (gate)
            {
                pairingToken = token ?? "";
                peers.Clear();
                selectedEndpointKey = null;
            }
            PublishSnapshot(true);
        }

        public void SetPinnedPeerId(string peerId)
        {
            lock (gate)
            {
                pinnedPeerId = peerId ?? "";
            }
            PublishSnapshot(true);
        }

        public void Dispose()
        {
            stopping = true;
            try { socket?.Close(); } catch { }
            try { receiveThread?.Join(1500); } catch { }
            try { announceThread?.Join(1500); } catch { }
            socket = null;
        }

        private void AnnounceLoop()
        {
            var multicastEndPoint = new IPEndPoint(IPAddress.Parse(MulticastGroup), DiscoveryPort);
            var broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

            while (!stopping)
            {
                try
                {
                    string token;
                    lock (gate)
                    {
                        token = pairingToken;
                    }

                    var announce = new DiscoveryAnnounce
                    {
                        Role = "vmt",
                        InstanceId = instanceId,
                        InstanceName = instanceName,
                        ProtoVersion = 1,
                        OscRecvPort = VmtOscRecvPort,
                        Capabilities = "pose",
                        PairingToken = token,
                    };
                    byte[] data = announce.Encode();
                    socket?.SendTo(data, multicastEndPoint);
                    socket?.SendTo(data, broadcastEndPoint);
                }
                catch (Exception ex)
                {
                    if (!stopping)
                    {
                        Console.WriteLine("# ZeroconfDiscovery announce: " + ex.Message);
                    }
                }

                PublishSnapshot(false);
                for (int i = 0; i < 10 && !stopping; i++)
                {
                    Thread.Sleep(100);
                }
            }
        }

        private void ReceiveLoop()
        {
            var buffer = new byte[2048];

            while (!stopping)
            {
                try
                {
                    EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    int length = socket.ReceiveFrom(buffer, ref remote);
                    var remoteEndPoint = remote as IPEndPoint;
                    if (remoteEndPoint == null)
                    {
                        continue;
                    }

                    DiscoveryAnnounce announce;
                    if (!DiscoveryAnnounce.TryParse(buffer, length, out announce))
                    {
                        continue;
                    }

                    if (!Admit(announce))
                    {
                        continue;
                    }

                    UpsertPeer(announce, remoteEndPoint.Address, DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    if (!stopping)
                    {
                        Console.WriteLine("# ZeroconfDiscovery receive: " + ex.Message);
                    }
                }
            }
        }

        private bool Admit(DiscoveryAnnounce announce)
        {
            if (announce.ProtoVersion != 1) return false;
            if (announce.Role != "jetson") return false;
            if (announce.InstanceId == instanceId) return false;
            if (string.IsNullOrWhiteSpace(announce.InstanceId)) return false;
            if (announce.OscRecvPort <= 0 || announce.OscRecvPort > 65535) return false;

            string token;
            lock (gate)
            {
                token = pairingToken;
            }
            if (!string.IsNullOrEmpty(token) && announce.PairingToken != token) return false;

            return true;
        }

        private void UpsertPeer(DiscoveryAnnounce announce, IPAddress sourceAddress, DateTime nowUtc)
        {
            lock (gate)
            {
                peers[announce.InstanceId] = new DiscoveryPeer
                {
                    InstanceId = announce.InstanceId,
                    InstanceName = announce.InstanceName,
                    Address = sourceAddress,
                    OscRecvPort = announce.OscRecvPort,
                    Capabilities = announce.Capabilities,
                    PairingToken = announce.PairingToken,
                    LastSeenUtc = nowUtc,
                };
            }
            PublishSnapshot(false);
        }

        private void PublishSnapshot(bool forceSelectedEvent)
        {
            DiscoverySnapshot snapshot;
            DiscoveryPeer selectedPeer;
            bool selectedChanged;

            lock (gate)
            {
                DateTime nowUtc = DateTime.UtcNow;
                selectedPeer = ChoosePeer(nowUtc);
                string newKey = selectedPeer == null ? null : EndpointKey(selectedPeer);
                selectedChanged = forceSelectedEvent || selectedEndpointKey != newKey;
                selectedEndpointKey = newKey;

                snapshot = new DiscoverySnapshot(
                    peers.Values.Select(p => p.Clone()).OrderBy(p => p.InstanceId, StringComparer.Ordinal).ToList(),
                    selectedPeer?.Clone(),
                    nowUtc,
                    peerTimeout,
                    pinnedPeerId,
                    pairingToken);
            }

            SnapshotChanged?.Invoke(snapshot);
            if (selectedChanged)
            {
                SelectedPeerChanged?.Invoke(selectedPeer?.Clone());
            }
        }

        private DiscoveryPeer ChoosePeer(DateTime nowUtc)
        {
            var livePeers = peers.Values
                .Where(p => nowUtc - p.LastSeenUtc <= peerTimeout)
                .OrderBy(p => p.InstanceId, StringComparer.Ordinal)
                .ToList();

            if (!string.IsNullOrEmpty(pinnedPeerId))
            {
                return livePeers.FirstOrDefault(p => p.InstanceId == pinnedPeerId);
            }

            return livePeers.FirstOrDefault();
        }

        private static string EndpointKey(DiscoveryPeer peer)
        {
            if (peer == null || peer.Address == null)
            {
                return null;
            }
            return peer.InstanceId + "|" + peer.Address + "|" + peer.OscRecvPort;
        }
    }
}
