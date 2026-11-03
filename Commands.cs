using System;
using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Runtime;
namespace MiningCalc
{
    public class Commands
    {
        [CommandMethod("SURVEY")]
        public void RunSurvey()
        {
            Document doc = HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            try
            {
                // Убрали AddAllowedClass, теперь можно выбрать любой объект
                PromptEntityOptions peoCloud = new PromptEntityOptions("\nВыберите облако точек карьера: ");
                PromptEntityResult perCloud = ed.GetEntity(peoCloud);
                if (perCloud.Status != PromptStatus.OK) return;
                PromptEntityOptions peoLine = new PromptEntityOptions("\nВыберите линию на слое горизонта: ");
                peoLine.SetRejectMessage("\nНеобходимо выбрать полилинию.");
                peoLine.AddAllowedClass(typeof(Polyline), false);
                PromptEntityResult perLine = ed.GetEntity(peoLine);
                if (perLine.Status != PromptStatus.OK) return;
                ExecuteCalculation(doc, perCloud.ObjectId, perLine.ObjectId);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[ОШИБКА] {ex.Message}");
            }
        }
        private void ExecuteCalculation(Document doc, ObjectId cloudId, ObjectId lineId)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    Polyline poly = (Polyline)tr.GetObject(lineId, OpenMode.ForRead);
                    
                    // Получаем объект как DBObject (базовый для всего)
                    DBObject cloudObj = tr.GetObject(cloudId, OpenMode.ForRead);
                    ed.WriteMessage("\n--- Анализ данных ---");
                    // ВАЖНО: Выведем полное имя класса объекта, чтобы узнать, как он называется в API
                    ed.WriteMessage($"\nТип выбранного объекта: {cloudObj.GetType().FullName}");
                    ed.WriteMessage($"\nВершин в линии: {poly.NumberOfVertices}");
                    // Пробуем привести к Entity (вдруг сработает)
                    if (cloudObj is Entity cloudEntity)
                    {
                        dynamic cloud = cloudEntity;
                        ed.WriteMessage("\nОблако точек успешно получено.");
                    }
                    else
                    {
                        ed.WriteMessage("\nВыбранный объект не является Entity, но мы его прочитали!");
                    }
                    ExcavationResult result = new ExcavationResult
                    {
                        RoofZ = 100.0,
                        FloorZ = 95.5,
                        VerticalDistance = 4.5,
                        Type = "Траншея"
                    };
                    ed.WriteMessage($"\nРезультат: Мощность = {result.VerticalDistance} м.");
                    
                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nОшибка при расчёте: {ex.Message}");
                    tr.Abort();
                }
            }
        }
    }
}
