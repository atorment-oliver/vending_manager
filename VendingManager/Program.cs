using System.Text.Json;
using VendingManager;

ApplicationConfiguration.Initialize();
var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VendingManager");
Directory.CreateDirectory(dataDir);
var configPath = Path.Combine(dataDir, "vending-config.json");
var databasePath = Path.Combine(dataDir, "consolidated.db");
var config = AppConfig.Load(configPath);
Application.Run(new MainForm(config, configPath, databasePath));