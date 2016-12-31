﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Xml;
using VRage.Plugins;

namespace LoadArms
{
	public class LoadArms : IPlugin
	{

		private const string
			launcherArgs = "-plugin LoadArms.exe",
			configFilePath = "Load-ARMS Config.json",
			dataFilePath = "Load-ARMS.data";

		public static void Main(string[] args)
		{
			try { LaunchSpaceEngineers(); }
			catch (Exception ex)
			{
				Logger.WriteLine(ex.ToString());
				Console.WriteLine(ex.ToString());
				Thread.Sleep(60000);
			}
		}

		private static void LaunchSpaceEngineers()
		{
			string myDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

			string launcher = myDirectory + "\\SpaceEngineers.exe";
			if (File.Exists(launcher))
				Process.Start(launcher, launcherArgs).Dispose();
			else
			{
				launcher = myDirectory + "\\SpaceEngineersDedicated.exe";
				if (File.Exists(launcher))
					Process.Start(launcher, launcherArgs).Dispose();
				else
					throw new Exception("Not in Space Engineers folder");
			}
		}

		[DataContract]
		public struct Config
		{
			[DataMember]
			public ModInfo[] GitHubMods;
		}

		[DataContract]
		public struct Data
		{
			[DataMember]
			public List<ModVersion> ModsCurrentVersions;
		}

		private Config _config;
		private Data _data;
		private List<IPlugin> _plugins = new List<IPlugin>();

		public LoadArms()
		{
			// self update?? maybe last?
			Load();
			UpdateMods();
			SaveData();
			LoadPlugins();
		}

		public void Dispose()
		{
			if (_plugins == null)
				return;

			foreach (IPlugin plugin in _plugins)
				plugin.Dispose();

			_config = default(Config);
			_plugins = null;
		}

		public void Init(object gameInstance)
		{
			foreach (IPlugin plugin in _plugins)
				plugin.Init(gameInstance);
		}

		public void Update()
		{
			foreach (IPlugin plugin in _plugins)
				plugin.Update();
		}

		private void Load()
		{
			LoadConfig();
			LoadData();
		}

		private void LoadConfig()
		{
			if (File.Exists(configFilePath))
			{
				try
				{
					DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(Config));
					using (FileStream file = new FileStream(configFilePath, FileMode.Open))
						_config = (Config)serializer.ReadObject(file);
					if (_config.GitHubMods != null)
						return;
					Logger.WriteLine("ERORR: Saved config is incomplete");
				}
				catch (Exception ex)
				{
					Logger.WriteLine("ERROR: Failed to read saved config:\n" + ex);
				}
			}

			_config.GitHubMods = new ModInfo[] { GetArmsDefaultInfo() };
			SaveConfig();
		}

		private void LoadData()
		{
			if (File.Exists(dataFilePath))
			{
				try
				{
					DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(Data));
					using (FileStream file = new FileStream(dataFilePath, FileMode.Open))
						_data = (Data)serializer.ReadObject(file);
					if (_data.ModsCurrentVersions != null)
						return;
					Logger.WriteLine("ERROR: Saved data is incomplete");
				}
				catch (Exception ex)
				{
					Logger.WriteLine("ERROR: Failed to read saved data:\n" + ex);
				}
			}

			_data.ModsCurrentVersions = new List<ModVersion>();
			_data.ModsCurrentVersions.Add(GetArmsDefaultVersion());
			SaveData();
		}

		private void SaveConfig()
		{
			DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(Config));
			using (XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(new FileStream(configFilePath, FileMode.Create), Encoding.UTF8, true, true))
				serializer.WriteObject(writer, _config);
		}

		private void SaveData()
		{
			DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(Data));
			using (XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(new FileStream(dataFilePath, FileMode.Create), Encoding.UTF8, true, true))
				serializer.WriteObject(writer, _data);
		}

		private void UpdateMods()
		{
			foreach (ModInfo mod in _config.GitHubMods)
			{
				Logger.WriteLine("mod: " + mod.repository);

				GitHubClient client = new GitHubClient(mod);

				for (int index = 0; index < _data.ModsCurrentVersions.Count; index++)
				{
					ModVersion current = _data.ModsCurrentVersions[index];
					if (current.mod.Equals(mod))
					{
						if (client.Update(ref current))
						{
							Logger.WriteLine("Updated");
							_data.ModsCurrentVersions[index] = current;
						}
						client = null;
					}
				}

				if (client != null)
				{
					ModVersion current = new ModVersion();
					current.mod = mod;
					if (client.Update(ref current))
					{
						Logger.WriteLine("New download");
						_data.ModsCurrentVersions.Add(current);
					}
				}
			}
		}

		private void LoadPlugins()
		{
			Type pluginType = typeof(IPlugin);

			foreach (ModVersion mod in _data.ModsCurrentVersions)
				if (mod.filePaths != null)
					foreach (string filePath in mod.filePaths)
					{
						if (!File.Exists(filePath))
						{
							throw new NotImplementedException("File missing: " + filePath);
						}
						string ext = Path.GetExtension(filePath);
						if (ext == ".dll")
						{
							Logger.WriteLine("Loading plugins from " + mod.mod.author + "/" + mod.mod.repository + "/" + filePath);

							Assembly assembly = Assembly.LoadFrom(filePath);
							if (assembly == null)
								Logger.WriteLine("ERROR: Could not load assembly: " + filePath);
							else
								foreach (Type t in assembly.ExportedTypes)
									if (pluginType.IsAssignableFrom(t))
										try { _plugins.Add((IPlugin)Activator.CreateInstance(t)); }
										catch (Exception ex) { Logger.WriteLine("ERROR: Could not create instance of type \"" + t.FullName + "\": " + ex); }
						}
					}
		}

		private ModInfo GetArmsDefaultInfo()
		{
			ModInfo result = new ModInfo();

			result.author = "Rynchodon";
			result.repository = "ARMS";

			return result;
		}

		private ModVersion GetArmsDefaultVersion()
		{
			ModVersion result = new ModVersion();

			result.mod = GetArmsDefaultInfo();
			result.filePaths = new string[] { "ARMS.dll", "ARMS - Release Notes.txt", "ExtendWhitelist.exe", "ExtendWhitelist.dll" , "ExtendWhitelist.log" };

			return result;
		}
		
	}
}
