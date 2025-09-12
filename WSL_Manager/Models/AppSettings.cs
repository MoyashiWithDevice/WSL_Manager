using System;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace WslManagerFramework.Models
{
    [Serializable]
    public class AppSettings
    {
        public bool MinimizeToTray { get; set; } = true;
        public bool RunAtStartup { get; set; } = true;

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WSLManager", "settings.xml");

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new AppSettings();

                var serializer = new XmlSerializer(typeof(AppSettings));
                using (var reader = new FileStream(SettingsPath, FileMode.Open))
                {
                    return (AppSettings)serializer.Deserialize(reader);
                }
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var serializer = new XmlSerializer(typeof(AppSettings));
                using (var writer = new FileStream(SettingsPath, FileMode.Create))
                {
                    serializer.Serialize(writer, this);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の保存に失敗しました: {ex.Message}", "エラー", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}