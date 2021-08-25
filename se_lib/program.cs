using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace se_lib {
    public class Program {
        public static void Main() {

            var thworker = new System.Threading.Thread(RunSTA) {
                IsBackground = true,
                Name = "Render1"
            };

            thworker.Start();
        }

        private static void RunSTA() {

            Queue<string> msgs = new Queue<string>();

            while (true) {
                try {
                    using (var client = new System.Net.Sockets.TcpClient()) {
                        client.Connect(System.Net.IPAddress.Parse("192.168.5.221"), 8001);

                        var blocks = new List<IEnumerable<byte>>();
                        var buf = new byte[4096];
                        using (var stream = client.GetStream()) {
                            while (true) {
                                try {
                                    if (stream.Read(buf, 0, 4) != 4) {
                                        break;
                                    }

                                    int nLength = BitConverter.ToInt32(buf, 0);
                                    int nread;

                                    for (nread = 0; nread < nLength;) {
                                        int tread = stream.Read(buf, 0, Math.Min(4096, nLength - nread));
                                        if (tread == 0) {
                                            nread = -1;
                                            break;
                                        }
                                        nread += tread;

                                        blocks.Add(buf.Take(tread));
                                        buf = new byte[4096];
                                    }

                                    if (nread < 0) break;

                                    var data = blocks.SelectMany(x => x);

                                    XObj xml = null;
                                    try {
                                        xml = X.FromXml(data.ToArray());
                                    } catch (System.Threading.ThreadAbortException) {
                                        throw;
                                    } catch (Exception ex) {
                                        msgs.Enqueue($"xml exception\r\n{ex}\r\n{Encoding.UTF8.GetString(data.ToArray())}");
                                    }
                                    blocks.Clear();

                                    if (!(xml is null)) {
                                        HandleMessage(msgs, xml);
                                    }

                                } catch (System.Threading.ThreadAbortException) {
                                    throw;
                                } catch (Exception ex) {
                                    msgs.Enqueue($"Stream Exception\n{ex.ToString()}\n");
                                }

                                while (msgs.Count != 0) {
                                    var msg = msgs.Dequeue();
                                    var response = Encoding.UTF8.GetBytes(msg);
                                    response = BitConverter.GetBytes((int)response.Length).Concat(response).ToArray();
                                    stream.Write(response, 0, response.Length);
                                }

                            }
                        }
                    }
                } catch (System.Threading.ThreadAbortException) {
                    break;
                } catch { }

                System.Threading.Thread.Sleep(1000);
            }
        }

        private static void HandleMessage(Queue<string> msgqueue, XObj xobj) {

            var c_params = new System.CodeDom.Compiler.CompilerParameters();
            c_params.GenerateInMemory = true;
            c_params.TreatWarningsAsErrors = false;
            c_params.GenerateExecutable = false;
            c_params.CompilerOptions = "/optimize";

            msgqueue.Enqueue("adding refs:\r\n");
            foreach (var cref in xobj.refs) {
                c_params.ReferencedAssemblies.Add(cref);
                msgqueue.Enqueue($"{cref}\r\n");
            }

            var provider = new Microsoft.CSharp.CSharpCodeProvider();
            var compile = provider.CompileAssemblyFromSource(c_params, xobj.code);

            if (compile.Errors.HasErrors) {
                msgqueue.Enqueue($"Compile failed:\n");
                foreach (System.CodeDom.Compiler.CompilerError err in compile.Errors) {
                    msgqueue.Enqueue($"#{err.ErrorNumber}: [{err.Line}] {err.ErrorText}\n");
                }

                return;
            }

            var mt = compile.CompiledAssembly.GetType("ExtRenderer.Program");
            if (mt is null) {
                msgqueue.Enqueue("Compile failed:\r\ntype ExtRenderer.Program is missing (in template?)\r\navailable types:\r\n");

                foreach (var type in compile.CompiledAssembly.GetTypes()) {
                    msgqueue.Enqueue($"{type.FullName}\r\n");
                }
                return;
            }
            var mi = mt.GetMethod("Main", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (mi is null) {
                msgqueue.Enqueue("Compile failed:\r\nProgram is missing Main() Function");
                return;
            }

            object instance;
            try {
                instance = Activator.CreateInstance(mt, msgqueue);
            } catch (System.Threading.ThreadAbortException) {
                throw;
            } catch (Exception ex) {
                msgqueue.Enqueue($"unable to instance Program:\r\n{ex}");
                return;
            }

            mi.Invoke(instance, new object[] { });
            msgqueue.Enqueue($"completed at {DateTime.Now}");
        }
    }
}
