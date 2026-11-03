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
                Document doc = HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    Editor ed = doc.Editor;
                    ed.WriteMessage("\n=== Маркшейдерский калькулятор v2.1 (nanoCAD 25+) ===");
                    ed.WriteMessage("\nПлагин успешно загружен. Введите SURVEY для запуска.");
                }
            }
            catch (System.Exception ex)
            {
                // Если при загрузке будет ошибка, она покажется здесь
                System.Windows.Forms.MessageBox.Show($"Ошибка: {ex.Message}", "MiningCalc Error");
            }
        }

        public void Terminate() { }
    }
}
