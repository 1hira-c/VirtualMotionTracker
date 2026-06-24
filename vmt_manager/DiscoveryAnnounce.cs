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
using System.Text;

namespace vmt_manager
{
    internal sealed class DiscoveryAnnounce
    {
        public const string Address = "/fitra/announce";
        public const string TypeTag = ",sssiiss";

        public string Role { get; set; }
        public string InstanceId { get; set; }
        public string InstanceName { get; set; }
        public int ProtoVersion { get; set; }
        public int OscRecvPort { get; set; }
        public string Capabilities { get; set; }
        public string PairingToken { get; set; }

        public byte[] Encode()
        {
            var data = new List<byte>(96);
            WritePaddedString(data, Address);
            WritePaddedString(data, TypeTag);
            WritePaddedString(data, Role ?? "");
            WritePaddedString(data, InstanceId ?? "");
            WritePaddedString(data, InstanceName ?? "");
            WriteInt32(data, ProtoVersion);
            WriteInt32(data, OscRecvPort);
            WritePaddedString(data, Capabilities ?? "");
            WritePaddedString(data, PairingToken ?? "");
            return data.ToArray();
        }

        public static bool TryParse(byte[] buffer, int length, out DiscoveryAnnounce announce)
        {
            announce = null;
            if (buffer == null || length <= 0 || length > buffer.Length)
            {
                return false;
            }

            int offset = 0;
            string address;
            string typeTag;
            string role;
            string instanceId;
            string instanceName;
            int protoVersion;
            int oscRecvPort;
            string capabilities;
            string pairingToken;

            if (!ReadPaddedString(buffer, length, ref offset, out address) || address != Address) return false;
            if (!ReadPaddedString(buffer, length, ref offset, out typeTag) || typeTag != TypeTag) return false;
            if (!ReadPaddedString(buffer, length, ref offset, out role)) return false;
            if (role != "jetson" && role != "vmt") return false;
            if (!ReadPaddedString(buffer, length, ref offset, out instanceId)) return false;
            if (!ReadPaddedString(buffer, length, ref offset, out instanceName)) return false;
            if (!ReadInt32(buffer, length, ref offset, out protoVersion)) return false;
            if (!ReadInt32(buffer, length, ref offset, out oscRecvPort)) return false;
            if (!ReadPaddedString(buffer, length, ref offset, out capabilities)) return false;
            if (!ReadPaddedString(buffer, length, ref offset, out pairingToken)) return false;
            if (offset != length) return false;

            announce = new DiscoveryAnnounce
            {
                Role = role,
                InstanceId = instanceId,
                InstanceName = instanceName,
                ProtoVersion = protoVersion,
                OscRecvPort = oscRecvPort,
                Capabilities = capabilities,
                PairingToken = pairingToken,
            };
            return true;
        }

        private static void WritePaddedString(List<byte> data, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
            data.AddRange(bytes);
            data.Add(0);
            while ((data.Count % 4) != 0)
            {
                data.Add(0);
            }
        }

        private static void WriteInt32(List<byte> data, int value)
        {
            unchecked
            {
                data.Add((byte)((value >> 24) & 0xff));
                data.Add((byte)((value >> 16) & 0xff));
                data.Add((byte)((value >> 8) & 0xff));
                data.Add((byte)(value & 0xff));
            }
        }

        private static bool ReadPaddedString(byte[] buffer, int length, ref int offset, out string value)
        {
            value = null;
            if (offset >= length)
            {
                return false;
            }

            int start = offset;
            while (offset < length && buffer[offset] != 0)
            {
                offset++;
            }

            if (offset >= length)
            {
                return false;
            }

            int stringLength = offset - start;
            offset++;
            while ((offset % 4) != 0)
            {
                if (offset >= length || buffer[offset] != 0)
                {
                    return false;
                }
                offset++;
            }

            value = Encoding.UTF8.GetString(buffer, start, stringLength);
            return true;
        }

        private static bool ReadInt32(byte[] buffer, int length, ref int offset, out int value)
        {
            value = 0;
            if (offset + 4 > length)
            {
                return false;
            }

            unchecked
            {
                value =
                    (buffer[offset] << 24) |
                    (buffer[offset + 1] << 16) |
                    (buffer[offset + 2] << 8) |
                    buffer[offset + 3];
            }
            offset += 4;
            return true;
        }
    }
}
