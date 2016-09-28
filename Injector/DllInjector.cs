﻿using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Injector
{
	class DllInjector
	{

		const string processNameSE = "SpaceEngineers", processNameSED = "SpaceEngineersDedicated";

		//static TextWriter _log = new StreamWriter("DllInjector.log");

		static void Main(string[] args)
		{
			Run();
			Thread.Sleep(10000);
		}

		private static void Run()
		{
			string myDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			string dllPath = myDirectory + "\\ExtendWhitelist.dll";

			if (!File.Exists(dllPath))
			{
				WriteLine("ExtendWhitelist.dll not found");
				return;
			}

			string dedicatedLauncher = myDirectory + "\\SpaceEngineersDedicated.exe";
			bool isDedicatedServer = File.Exists(dedicatedLauncher);

			Process process = GetGameProcess(isDedicatedServer);

			if (process == null)
			{
				if (isDedicatedServer)
					Process.Start(dedicatedLauncher);
				else
				{
					string pathToSteam = myDirectory;
					for (int i = 0; i < 4; i++)
						pathToSteam = Path.GetDirectoryName(pathToSteam);
					pathToSteam += "\\Steam.exe";

					if (File.Exists(pathToSteam))
						Process.Start(pathToSteam, "-applaunch 244850");
					else
					{
						string launcher = myDirectory + "\\SpaceEngineers.exe";
						if (File.Exists(launcher))
						{
							WriteLine("Alternate launch");
							Process.Start(launcher);
							Thread.Sleep(100);
						}
						else
						{
							WriteLine("Game not found");
							return;
						}
					}
				}

				WriteLine("Game launched");
			}

			process = WaitForGameStart(isDedicatedServer);
			if (process == null)
				return;

			Thread.Sleep(1000);

			Inject(process, dllPath);

			TextReader reader = new StreamReader(Extender.WhitelistExtender.LOG_NAME);
			string line;
			while ((line = reader.ReadLine()) != null)
				WriteLine(line, true);
			reader.Close();
		}

		private static Process GetGameProcess(bool isDedicatedServer)
		{
			Process[] processes = Process.GetProcessesByName(isDedicatedServer ? processNameSED : processNameSE);

			Process newest = null;
			foreach (Process process in processes)
				if (newest == null || process.StartTime > newest.StartTime)
					newest = process;

			return newest;
		}

		private static Process WaitForGameStart(bool isDedicatedServer)
		{
			for (int c = 0; c < 6000; c++)
			{
				Thread.Sleep(100);
				Process process = GetGameProcess(isDedicatedServer);

				if (process == null)
				{
					if (c > 600)
						break;
				}
				else if (!string.IsNullOrWhiteSpace(process.MainWindowTitle))
				{
					if (isDedicatedServer && (process.MainWindowTitle.Contains("Select Instance") || process.MainWindowTitle.Contains("configurator")))
					{
						WriteLine("Configurator is running");
						process.WaitForExit();
						return WaitForGameStart(isDedicatedServer);
					}
					return process;
				}
			}

			WriteLine("Game did not start");
			return null;
		}
		
		private static bool Inject(Process process, string dllPath)
		{
			IntPtr hProcess = Kernel32Wrapper.OpenProcess(0x2 | 0x8 | 0x10 | 0x20 | 0x400, true, (uint)process.Id);

			if (hProcess == IntPtr.Zero)
			{
				WriteLine("Failed to get process handle");
				return false;
			}

			IntPtr lpAddress = IntPtr.Zero, hThread = IntPtr.Zero;

			try
			{
				IntPtr lpStartAddress = Kernel32Wrapper.GetProcAddress(Kernel32Wrapper.GetModuleHandle("kernel32.dll"), "LoadLibraryA");

				if (lpStartAddress == IntPtr.Zero)
				{
					WriteLine("Failed to get address for load library");
					return false;
				}

				lpAddress = Kernel32Wrapper.VirtualAllocEx(hProcess, IntPtr.Zero, (IntPtr)dllPath.Length, 0x1000 | 0x2000, 0x40);

				if (lpAddress == IntPtr.Zero)
				{
					WriteLine("Failed to allocate memory");
					return false;
				}

				if (!Kernel32Wrapper.WriteProcessMemory(hProcess, lpAddress, Encoding.ASCII.GetBytes(dllPath), (IntPtr)dllPath.Length))
				{
					WriteLine("Failed to write dll name");
					return false;
				}

				hThread = Kernel32Wrapper.CreateRemoteThread(hProcess, IntPtr.Zero, IntPtr.Zero, lpStartAddress, lpAddress, 0, IntPtr.Zero);

				if (hThread == IntPtr.Zero)
				{
					WriteLine("Failed to create loading thread");
					return false;
				}

				if (Kernel32Wrapper.WaitForSingleObject(hThread, 10000) != 0)
				{
					WriteLine("Loading thread timed out");
					return false;
				}

				WriteLine("Dll injected");

				Kernel32Wrapper.LoadLibrary(dllPath);

				lpStartAddress = Kernel32Wrapper.GetProcAddress(Kernel32Wrapper.GetModuleHandle(dllPath), "RunInSEProcess");

				if (lpStartAddress == IntPtr.Zero)
				{
					WriteLine("Failed to get RunInSEProcess address");
					return false;
				}

				Kernel32Wrapper.CloseHandle(hThread);

				hThread = Kernel32Wrapper.CreateRemoteThread(hProcess, IntPtr.Zero, IntPtr.Zero, lpStartAddress, IntPtr.Zero, 0, IntPtr.Zero);

				if (hThread == IntPtr.Zero)
				{
					WriteLine("Failed to create run thread");
					return false;
				}

				WriteLine("Waiting for game to finish loading");

				while (Kernel32Wrapper.WaitForSingleObject(hThread, 1000) != 0)
					if (process.HasExited)
					{
						WriteLine("Game terminated before it finished loading");
						return false;
					}
			}
			finally
			{
				if (hThread != IntPtr.Zero)
					Kernel32Wrapper.CloseHandle(hThread);

				if (lpAddress != IntPtr.Zero)
					Kernel32Wrapper.VirtualFreeEx(hProcess, lpAddress, IntPtr.Zero, 0x8000);

				Kernel32Wrapper.CloseHandle(hProcess);
			}

			return true;
		}

		private static void WriteLine(string line, bool skipMemeberName = false, [CallerMemberName] string memberName = null)
		{
			if (!skipMemeberName)
				line = memberName + ": " + line;
			Console.WriteLine(line);
			//_log.WriteLine(line);
			//_log.Flush();
		}

	}
}