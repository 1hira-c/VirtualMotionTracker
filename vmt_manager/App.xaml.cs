using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace vmt_manager
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
        // Phase 15.5 #1 fix: SteamVR の auto-launch + ユーザ手動起動の同時発生で
        //  Manager が二重起動するケースを防ぐ named mutex。インストール毎に分離したいが
        //  app_key と揃えるのが分かりやすいので同じ識別子を使う。
        private const string SingleInstanceMutexName = "Global\\1hira.fitra.vmt_manager.singleinstance";
        private Mutex singleInstanceMutex;

        public App()
        {
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            //コマンドラインモード (install/uninstall/setroommatrix) は完了して即終了する
            //一回操作なので、共存しても害は少ない (mutex を取らずに通す)。対話起動のみ排他。
            bool isInteractive = (e.Args == null || e.Args.Length == 0);

            if (isInteractive)
            {
                bool createdNew;
                singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
                if (!createdNew)
                {
                    //既に別プロセスの Manager が走っている → 黙って終了
                    //(ダイアログを出すと auto-launch でうるさいので)
                    singleInstanceMutex = null;
                    Shutdown(0);
                    return;
                }
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (singleInstanceMutex != null)
            {
                try { singleInstanceMutex.ReleaseMutex(); } catch { }
                singleInstanceMutex.Dispose();
                singleInstanceMutex = null;
            }
            base.OnExit(e);
        }

        //Thread.Sleep(100)は、入れないとnotepadが起動しないので入れている

        static bool TaskScheduler_UnobservedTaskException_Recorded = false;
        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            if (!TaskScheduler_UnobservedTaskException_Recorded)
            {
                TaskScheduler_UnobservedTaskException_Recorded = true;
                exceptionHandler(e.Exception, "UnobservedTaskException.log");
            }
            Thread.Sleep(100);
        }

        static bool CurrentDomain_UnhandledException_Recorded = false;
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (!CurrentDomain_UnhandledException_Recorded)
            {
                CurrentDomain_UnhandledException_Recorded = true;
                exceptionHandler(e.ExceptionObject, "UnhandledException.log");
            }
            Thread.Sleep(100);
        }

        static bool App_DispatcherUnhandledException_Recorded = false;
        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            if (!App_DispatcherUnhandledException_Recorded)
            {
                App_DispatcherUnhandledException_Recorded = true;
                exceptionHandler(e.Exception, "DispatcherUnhandledException.log");
            }
            Thread.Sleep(100);
        }

        static void exceptionHandler(Object e, string filename)
        {
            try
            {
                Exception exception = e as Exception;
                if (exception != null)
                {
                    string msg = "VMT Manager\n";
                    msg += "=============================\n";
                    msg += "例外が発生しました。(Exception)\n";
                    msg += "=============================\n";
                    msg += exception.Message + "\n";
                    msg += "=============================\n";
                    msg += exception.StackTrace + "\n";
                    msg += "=============================\n";
                    File.WriteAllText(filename, msg, new UTF8Encoding(false));
                    System.Diagnostics.Process.Start(filename);
                }
                else
                {
                    string msg = "不明な例外が発生しました。(Unknown Exception)\n";
                    File.WriteAllText(filename, msg, new UTF8Encoding(false));
                    System.Diagnostics.Process.Start(filename);
                }
            }
            catch (Exception)
            {
                //Do noting
            }
        }
    }
}
