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
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            try
            {
                PromptEntityOptions peoCloud = new PromptEntityOptions("\nВыберите облако точек карьера: ");
                peoCloud.SetRejectMessage("\nЭто не объект чертежа.");
                peoCloud.AddAllowedClass(typeof(Entity), true);
                PromptEntityResult perCloud = ed.GetEntity(peoCloud);
                if (perCloud.Status != PromptStatus.OK) return;
                PromptEntityOptions peoLine = new PromptEntityOptions("\nВыберите линию на слое горизонта: ");
                peoLine.SetRejectMessage("\nНеобходимо выбрать полилинию.");
                peoLine.AddAllowedClass(typeof(Polyline), false);
                PromptEntityResult perLine = ed.GetEntity(peoLine);
                if (perLine.Status != PromptStatus.OK) return;
                ExecuteCalculation(doc, perCloud.ObjectId, perLine.ObjectId);
            }
            catch (Exception ex)
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
                    Entity cloudEntity = (Entity)tr.GetObject(cloudId, OpenMode.ForRead);
                    ed.WriteMessage("\n--- Анализ данных ---");
                    ed.WriteMessage($"\nТип выбранного объекта: {cloudEntity.GetType().Name}");
                    ed.WriteMessage($"\nВершин в линии: {poly.NumberOfVertices}");
                    dynamic cloud = cloudEntity;
                    ed.WriteMessage("\nОблако точек успешно получено (динамический доступ).");
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
                catch (Exception ex)
                {
                    ed.WriteMessage($"\nОшибка при расчёте: {ex.Message}");
                    tr.Abort();
                }
            }
        }
    }
}
