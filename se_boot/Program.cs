using System;

namespace se_boot {
    class Program {

        static void Main(string[] args) {
            string processName = "SpaceEngineers";
            int processId;
            while (true) {
                var processes = System.Diagnostics.Process.GetProcessesByName(processName);
                if (processes.Length == 1) {
                    processId = processes[0].Id;
                    break;
                }

                if (processes.Length == 0) {
                    Console.Write($"waiting for process '{processName}' ");
                    for (int i = 0; i < 3; i++) {
                        System.Threading.Thread.Sleep(333);
                        Console.Write(".");
                    }
                    Console.CursorLeft = 0;
                    continue;
                }

                Console.WriteLine("found multiple processes, please select target:");
                for (int i = 0; i < processes.Length; i++) {
                    Console.WriteLine("[{0}]: Id={1}, Name={2}", i + 1, processes[i].Id, processes[i].ProcessName);
                }
                Console.WriteLine("[e]: exit");

                do {
                    string input = Console.ReadLine();
                    if (int.TryParse(input, out processId) && processId <= processes.Length && processId > 0) {
                        break;
                    }
                    if (input == "e") return;

                    Console.WriteLine("invalid input, try again:");
                } while (true);
                processId = processes[processId].Id;
                break;
            }

            try {
                Console.WriteLine("injecting into {0} ({1})", processName, processId);
                var inj = new Inject(processId);
                inj.InjectManagedDll(System.IO.Path.Combine(Environment.CurrentDirectory, @"se_lib.dll"), "se_lib.Program", "Main");
                Console.WriteLine("done");

            } catch (Exception ex) {
                Console.WriteLine("inject failed: {0}", ex);
            }

            System.Threading.Thread.Sleep(1000);
        }
    }
}
