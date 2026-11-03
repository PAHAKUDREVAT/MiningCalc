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
ed.WriteMessage("\n=== Маркшейдерский калькулятор (v3.0 - LAS Reader) ===");
ed.WriteMessage("\nПлагин успешно загружен. Введите SURVEY для запуска.");
}
}
catch (System.Exception ex)
{
// Если что-то пойдет не так при старте
System.Windows.Forms.MessageBox.Show($"Ошибка при загрузке: {ex.Message}");
}
}

public void Terminate() { }
}
}
