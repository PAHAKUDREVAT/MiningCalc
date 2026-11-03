using System;
using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using Teigha.Runtime;

namespace MiningCalc
{
    public class PluginEntry : IExtensionApplication
    {
        public void Initialize()
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    Editor ed = doc.Editor;
                    ed.WriteMessage("\n=== Маркшейдерский калькулятор v2.1 (nanoCAD 25+) ===");
                    ed.WriteMessage("\nПлагин успешно загружен. Введите SURVEY для запуска.");
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    $"Ошибка инициализации плагина:\n{ex.Message}\n\nСтек:\n{ex.StackTrace}", 
                    "MiningCalc Error", 
                    System.Windows.Forms.MessageBoxButtons.OK, 
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        public void Terminate() { }
    }
}