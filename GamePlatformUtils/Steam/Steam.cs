﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GamePlatformUtils.Steam
{
    public class Steam : Platform
    {
        private List<string> _LibraryFolders = new List<string>();

        public List<string> LibraryFolders { get { return _LibraryFolders; } set { _LibraryFolders = value; } }

        public event EventHandler BigPictureStateChanged;

        private bool _BigPictureOpen = false;

        public bool BigPictureOpen { get { return _BigPictureOpen; }
            set
            {
                bool changed = false;
                if (value != _BigPictureOpen)
                    changed = true;

                _BigPictureOpen = value;

                if (changed)
                    BigPictureStateChanged?.Invoke(this, new EventArgs());
            }
        }

        public Steam() : base()
        {
            
        }

        protected string RetrieveInstallPath()
        {
            string path = null;

            if (!Utils.IsLinux)
            {
                string[] reg_values = { @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Valve\Steam" };
                foreach(string reg in reg_values)
                {
                    try
                    {
                        string install_path = (string)Microsoft.Win32.Registry.GetValue(reg, "InstallPath", null);
                        if (!string.IsNullOrWhiteSpace(install_path) && File.Exists(Path.Combine(install_path, "Steam.exe")))
                        {
                            path = install_path;
                            break;
                        }
                    }
                    catch(Exception exc) { }
                }
            }

            return path;
        }

        public override void LoadData()
        {
            if (string.IsNullOrWhiteSpace(this.InstallPath))
                this.InstallPath = this.RetrieveInstallPath();

            base.LoadData();
            this.SetupUpdateListeners();
        }

        public void LoadRegistryValues()
        {

        }

        private List<FileSystemWatcher> Watchers = new List<FileSystemWatcher>();
        protected virtual void SetupUpdateListeners()
        {
            foreach (string library in this.LibraryFolders)
            {
                FileSystemWatcher watcher = new FileSystemWatcher(library, "appmanifest_*.acf") {
                    EnableRaisingEvents = true
                };
                watcher.Changed += LibraryAppFileChanged;
                watcher.Created += LibraryAppFileChanged;
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size;
                Watchers.Add(watcher);
            }

            string main_path = Path.Combine(this.InstallPath, Utils.IsLinux ? "steamapps" : "SteamApps");
            FileSystemWatcher lib_watcher = new FileSystemWatcher(main_path, "libraryfolders.vdf")
            {
                EnableRaisingEvents = true
            };
            lib_watcher.Changed += LibraryFileChanged;
            lib_watcher.Created += LibraryFileChanged;
            lib_watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size;
            Watchers.Add(lib_watcher);

        }

        private void LibraryAppFileChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Changed)
            {
                string ID = Path.GetFileNameWithoutExtension(e.Name).Substring("appmanifest_".Length);
                if (this.Games.ContainsKey(ID))
                    this.Games[ID].Reload();
            }
            else if (e.ChangeType == WatcherChangeTypes.Created)
            {
                SteamGame game = new SteamGame(e.FullPath);
                if (!this.Games.ContainsKey(game.ID))
                {
                    this.Games.Add(game.ID, game);
                    this.GameAddedTrigger(new GameEventArgs { Game = game });
                }
            }
        }
        private void LibraryFileChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType.HasFlag(WatcherChangeTypes.Changed) || e.ChangeType.HasFlag(WatcherChangeTypes.Created))
            {
                this.LoadAdditionalLibraries(e.FullPath);
            }
        }

        public override void LoadGames()
        {
            this.Games.Clear();

            string main_path = Path.Combine(this.InstallPath, Utils.IsLinux ? "steamapps" : "SteamApps");
            this.LoadGames(main_path);
            string lib_path;
            if (File.Exists(lib_path = Path.Combine(main_path, "libraryfolders.vdf")))
            {
                this.LoadAdditionalLibraries(lib_path);
            }
        }

        public void LoadAdditionalLibraries(string lib_path)
        {
            Dictionary<string, object> libraries = new KeyValue(
                                new FileStream(lib_path, FileMode.Open, FileAccess.Read)
                                ).Items["libraryfolders"] as Dictionary<string, object>;

            for (int i = 1; libraries?.ContainsKey(i.ToString()) ?? false; i++)
            {
                string add_lib_path = Path.Combine(libraries[i.ToString()] as string, Utils.IsLinux ? "steamapps" : "SteamApps");
                if (!this.LibraryFolders.Contains(add_lib_path))
                    this.LoadGames(add_lib_path);
            }
        }

        public void LoadGames(string path)
        {
            if (path == null || !Directory.Exists(path))
                return;

            this.LibraryFolders.Add(path);

            foreach(string file in Directory.EnumerateFiles(path, "appmanifest_*.acf"))
            {
                SteamGame game = new SteamGame(file);
                if (!this.Games.ContainsKey(game.ID))
                    this.Games.Add(game.ID, game);
                else
                    Console.WriteLine("Game with ID '{0}' already exists in the Games dictionary!", game.ID);
            }
        }
    }
}