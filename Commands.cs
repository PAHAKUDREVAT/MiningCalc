using System;
using System.Collections.Generic;
using System.Linq;
using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
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
                // 1. Выбор облака карьера
                var peoQuarry = new PromptEntityOptions("\nВыберите облако точек КАРЬЕРА: ");
                var perQuarry = ed.GetEntity(peoQuarry);
                if (perQuarry.Status != PromptStatus.OK) return;
                // 2. Выбор облака выработки
                var peoExc = new PromptEntityOptions("\nВыберите облако точек ВЫРАБОТКИ: ");
                var perExc = ed.GetEntity(peoExc);
                if (perExc.Status != PromptStatus.OK) return;
                // 3. Выбор линии забоя
                var peoLine = new PromptEntityOptions("\nВыберите линию ЗАБОЯ: ");
                peoLine.AddAllowedClass(typeof(Polyline), false);
                var perLine = ed.GetEntity(peoLine);
                if (perLine.Status != PromptStatus.OK) return;
                // 4. Указание точки выпуска
                var ppoCenter = new PromptPointOptions("\nУкажите точку выпуска (центр захватки): ");
                var pprCenter = ed.GetPoint(ppoCenter);
                if (pprCenter.Status != PromptStatus.OK) return;
                ExecuteCalculation(doc, perQuarry.ObjectId, perExc.ObjectId, perLine.ObjectId, pprCenter.Value);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[ОШИБКА] {ex.Message}");
            }
        }
        private void ExecuteCalculation(Document doc, ObjectId quarryId, ObjectId excId, ObjectId lineId, Point3d centerPoint)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    ed.WriteMessage("\n--- Анализ данных ---");
                    // 1. Построение вектора оси забоя
                    var faceLine = (Polyline)tr.GetObject(lineId, OpenMode.ForRead);
                    Point3d p1 = faceLine.GetPoint3dAt(0);
                    Point3d p2 = faceLine.GetPoint3dAt(1);
                    Vector3d axisDir = (p2 - p1).GetNormal();
                    Vector3d normalDir = new Vector3d(-axisDir.Y, axisDir.X, 0);
                    // 2. Получение точек из облаков
                    Entity excEntity = (Entity)tr.GetObject(excId, OpenMode.ForRead);
                    Entity quarryEntity = (Entity)tr.GetObject(quarryId, OpenMode.ForRead);
                    List<Point3d>? excPoints = TryGetPointsFromEntity(excEntity, ed);
                    List<Point3d>? quarryPoints = TryGetPointsFromEntity(quarryEntity, ed);
                    if (excPoints == null || quarryPoints == null)
                    {
                        ed.WriteMessage("\n[ПРЕДУПРЕЖДЕНИЕ] Не удалось автоматически извлечь точки из облаков.");
                        tr.Abort();
                        return;
                    }
                    ed.WriteMessage($"\nТочек в выработке: {excPoints.Count}, в карьере: {quarryPoints.Count}");
                    // 3. Фильтрация и поиск минимальной высоты
                    double halfWidth = 7.5;
                    double length = 20.0;
                    double minHeight = double.MaxValue;
                    Point3d bestExc = Point3d.Origin;
                    Point3d bestQuarry = Point3d.Origin;
                    int pointsInZone = 0;
                    foreach (var excPt in excPoints)
                    {
                        Vector3d vec = excPt - centerPoint;
                        double distAlongAxis = vec.DotProduct(axisDir);
                        double distAlongNormal = vec.DotProduct(normalDir);
                        // Проверка попадания в зону 15х20
                        if (distAlongAxis >= 0 && distAlongAxis <= length && Math.Abs(distAlongNormal) <= halfWidth)
                        {
                            pointsInZone++;
                            // Поиск ближайшей точки карьера по XY
                            var quarryPt = quarryPoints
                                .Where(q => Math.Abs(q.X - excPt.X) < 0.3 && Math.Abs(q.Y - excPt.Y) < 0.3)
                                .OrderBy(q => Math.Abs(q.Z - excPt.Z))
                                .FirstOrDefault();
                            if (quarryPt != Point3d.Origin)
                            {
                                double height = excPt.Z - quarryPt.Z;
                                if (height > 0 && height < minHeight)
                                {
                                    minHeight = height;
                                    bestExc = excPt;
                                    bestQuarry = quarryPt;
                                }
                            }
                        }
                    }
                    // 4. Вывод результатов
                    if (minHeight != double.MaxValue)
                    {
                        ed.WriteMessage($"\n--- РЕЗУЛЬТАТ ---");
                        ed.WriteMessage($"\nТочек в зоне захватки: {pointsInZone}");
                        ed.WriteMessage($"\nМинимальная мощность: {minHeight:F3} м.");
                        ed.WriteMessage($"\nВерх выработки: Z = {bestExc.Z:F3} (X:{bestExc.X:F2}, Y:{bestExc.Y:F2})");
                        ed.WriteMessage($"\nНиз карьера:    Z = {bestQuarry.Z:F3} (X:{bestQuarry.X:F2}, Y:{bestQuarry.Y:F2})");
                    }
                    else
                    {
                        ed.WriteMessage("\nВ заданной зоне не найдено корректных пар точек (выработка над карьером).");
                    }
                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nОшибка при расчёте: {ex.Message}");
                    tr.Abort();
                }
            }
        }
        private List<Point3d>? TryGetPointsFromEntity(Entity ent, Editor ed)
        {
            try
            {
                dynamic dynEnt = ent;
                var points = dynEnt.GetPoints();
                if (points != null)
                {
                    var result = new List<Point3d>();
                    foreach (var p in points)
                    {
                        result.Add((Point3d)p);
                    }
                    return result;
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[DEBUG] Ошибка извлечения точек (GetPoints): {ex.Message}");
            }
            try
            {
                dynamic dynEnt = ent;
                var points = dynEnt.Points;
                if (points != null)
                {
                    var result = new List<Point3d>();
                    foreach (var p in points)
                    {
                        result.Add((Point3d)p);
                    }
                    return result;
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[DEBUG] Ошибка извлечения точек (Points): {ex.Message}");
            }
            return null;
        }
    }
}
