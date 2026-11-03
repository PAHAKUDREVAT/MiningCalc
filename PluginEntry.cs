using System;
using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using Teigha.Runtime;
using System.Windows.Forms;
namespace MiningCalc
{
    public class PluginEntry : IExtensionApplication
    {
        public void Initialize()
        {
            try
            {
                // Первое окно: доказывает, что DLL загрузилась
                MessageBox.Show("Плагин начал загрузку!", "Тест", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Document doc = HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    Editor ed = doc.Editor;
                    ed.WriteMessage("\n=== Маркшейдерский калькулятор v2.1 (nanoCAD 25+) ===");
                }
                
                // Второе окно: доказывает, что инициализация прошла без ошибок
                MessageBox.Show("Плагин успешно инициализирован! Теперь введите SURVEY.", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (System.Exception ex)
            {
                // Если внутри что-то падает, мы это увидим
                MessageBox.Show($"ОШИБКА: {ex.Message}\n\nСтек:\n{ex.StackTrace}", "Ошибка плагина", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        public void Terminate() { }
    }
}
