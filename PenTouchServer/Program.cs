using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Threading;


namespace PenTouchServer
{
	class Program
	{
		private static Mutex mut = new Mutex();
		private static List<TcpClient> array;
		//private static int clientNum;

		static void Main(string[] args)
		{
			//clientNum = 0;
			array = new List<TcpClient>();
			TcpListener socketListener = new TcpListener(21122);
			socketListener.Start();
			Console.WriteLine("begin accept");
			socketListener.BeginAcceptSocket(new AsyncCallback(DoAcceptSocketCallback), socketListener);
			while (true)
			{
				Console.WriteLine("in while loop");
				Thread.Sleep(1000);
				mut.WaitOne();
				{
					Console.WriteLine("client number : " + array.Count);
					for (int i = 0; i < array.Count(); i++)
					{
						NetworkStream stream;
						try
						{
							stream = array[i].GetStream();
						}
						catch (Exception E)
						{
							Console.WriteLine("Stream make failed");
							array.RemoveAt(i);
							i--;
							continue;
						}
						Byte[] bytes = new Byte[4096];
						int count;
						stream.ReadTimeout = 100;
						String data = "";
						try
						{
							while ((count = stream.Read(bytes, 0, bytes.Length)) != 0) 
							{
                                for (int j = 0; j < array.Count(); j++)
                                {
                                    if (i != j)
                                    {
                                        try
                                        {
                                            array[j].GetStream().Write(bytes, 0, count);
                                        }
                                        catch (Exception E)
                                        {
                                            Console.WriteLine("write failed");
                                            array.RemoveAt(j);
                                            j--;
                                        }
                                    }
                                }
							}
						}
						catch (Exception E)
						{
							//Console.WriteLine(i + " : " + E.Message);
						}
					}
				}
				mut.ReleaseMutex();
			}
		}

		public static void DoAcceptSocketCallback(IAsyncResult ar)
		{
			Console.WriteLine("accept callback called");
			TcpListener socketListener = (TcpListener)ar.AsyncState;
			mut.WaitOne();
			{
				Console.WriteLine("callback got mutex");
				TcpClient client = socketListener.EndAcceptTcpClient(ar);
				array.Add(client);
				//clientNum++;
				socketListener.BeginAcceptSocket(new AsyncCallback(DoAcceptSocketCallback), socketListener);
			}
			mut.ReleaseMutex();
		}
	}
}
