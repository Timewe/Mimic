﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;
using WebSocketSharp.Server;

namespace MimicConduit
{
    class Program : ApplicationContext
    {
        public static string APP_NAME = "Mimic"; // For boot identification
        public static string VERSION = "1.2.0";

        private WebSocketServer server;
        private List<LeagueSocketBehavior> behaviors = new List<LeagueSocketBehavior>();
        private NotifyIcon trayIcon;
        private bool connected = false;
        private LeagueMonitor leagueMonitor;
        private RegistryKey bootKey = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
        private MenuItem startOnBootMenuItem;

        private Program(string lcuPath)
        {
            // Start the websocket server. It will not actually do anything until we add a behavior.
            server = new WebSocketServer(8182);
                
            try
            {
                server.Start();
            }
            catch (System.Net.Sockets.SocketException e)
            {
                MessageBox.Show($"Error code {e.ErrorCode.ToString()}: '{e.Message}'", "Unable to start server", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            trayIcon = new NotifyIcon()
            {
                Icon = new Icon(GetType(), "mimic.ico"),
                Visible = true,
                BalloonTipTitle = "Mimic",
            };

            if (!startsOnBoot) // If starting on boot, boot silently.
            {
                trayIcon.BalloonTipText = "Mimic will run in the background. Right-Click the tray icon for more info.";
                trayIcon.ShowBalloonTip(5000);
            }
            else if (bootKey.GetValue(APP_NAME).ToString() != Application.ExecutablePath) // Was our application moved?
                bootKey.SetValue(APP_NAME, Application.ExecutablePath); // Make sure the application startup location is correct

            UpdateMenuItems();

            // Start monitoring league.
            leagueMonitor = new LeagueMonitor(lcuPath, onLeagueStart, onLeagueStop);
        }


        private bool startsOnBoot
        {
            get
            {
                return bootKey.GetValue(APP_NAME) != null;
            }
        }

        private void ToggleStartOnBoot()
        {
            if (!startsOnBoot)
                bootKey.SetValue(APP_NAME, Application.ExecutablePath);
            else bootKey.DeleteValue(APP_NAME, false);
            
            trayIcon.BalloonTipText = $"Mimic {(startsOnBoot ? "will now" : "won't")} start with Windows from now on.";
            trayIcon.ShowBalloonTip(1000);

            // Update menu state
            startOnBootMenuItem.Checked = startsOnBoot;
        }

        private void UpdateMenuItems()
        {
            var aboutMenuItem = new MenuItem($"Mimic v{VERSION} - {(connected ? "Connected" : "Disconnected")}");
            aboutMenuItem.Enabled = false;

            var ipMenuItem = new MenuItem("Local IP Address: " + FindLocalIP());
            ipMenuItem.Enabled = false;

            startOnBootMenuItem = new MenuItem("Start with Windows", (s, e) => ToggleStartOnBoot());
            startOnBootMenuItem.Checked = startsOnBoot;

            var quitMenuItem = new MenuItem("Quit", (a, b) => Application.Exit());
            trayIcon.ContextMenu = new ContextMenu(new MenuItem[] { aboutMenuItem, ipMenuItem, startOnBootMenuItem, quitMenuItem });
        }

        private string FindLocalIP()
        {
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                socket.Connect("8.8.8.8", 65530);
                IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                return endPoint.Address.ToString();
            }
        }

        private void onLeagueStart(string lockfileContents)
        {
            Console.WriteLine("League Started.");
            trayIcon.BalloonTipText = "Connected to League. Visit http://mimic.molenzwiebel.xyz to control your client remotely.";
            trayIcon.ShowBalloonTip(2000);
            connected = true;
            UpdateMenuItems();

            var parts = lockfileContents.Split(':');
            var port = int.Parse(parts[2]);
            server.AddWebSocketService("/league", () =>
            {
                var behavior = new LeagueSocketBehavior(port, parts[3]);
                behaviors.Add(behavior);
                return behavior;
            });

            MakeDiscoveryRequest("PUT", "{ \"internal\": \"" + FindLocalIP() + "\" }");
        }

        private void onLeagueStop()
        {
            Console.WriteLine("League Stopped.");
            trayIcon.BalloonTipText = "Disconnected from League.";
            trayIcon.ShowBalloonTip(1000);
            connected = false;
            UpdateMenuItems();

            // This will cleanup the pending connections too.
            behaviors.ForEach(x => x.Destroy());
            behaviors.Clear();
            server.RemoveWebSocketService("/league");

            MakeDiscoveryRequest("DELETE", "{}");
        }

        /// Makes an http request to the discovery server to announce or denounce our IP pairs.
        static void MakeDiscoveryRequest(string method, string body)
        {
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.ContentType] = "application/json";
                try { client.UploadString("http://discovery.mimic.molenzwiebel.xyz/discovery", method, body); } catch { }
            }
        }

        [STAThread]
        static void Main()
        {
            try
            {
                using (new SingleGlobalInstance(500)) // Wait 500 seconds max for other programs to stop
                {
                    var lcuPath = LeagueMonitor.GetLCUPath();
                    if (lcuPath == null)
                    {
                        MessageBox.Show("Could not determine path to LCU!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                        return; // Abort
                    }

                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new Program(lcuPath));
                }
            }
            catch (TimeoutException)
            {
                MessageBox.Show("Mimic Conduit is already running!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }
    }
}
