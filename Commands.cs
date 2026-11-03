using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
                var peoQuarry = new PromptEntityOptions("\nВыберите облако точек КАРЬЕРА: ");
                var perQuarry = ed.GetEntity(peoQuarry);
                if (perQuarry.Status != PromptStatus.OK) return;
                var peoExc = new PromptEntityOptions("\nВыберите облако точек ВЫРАБОТКИ: ");
                var perExc = ed.GetEntity(peoExc);
                if (perExc.Status != PromptStatus.OK) return;
                var peoLine = new PromptEntityOptions("\nВыберите линию ЗАБОЯ: ");
                peoLine.AddAllowedClass(typeof(Polyline), false);
                var perLine = ed.GetEntity(peoLine);
                if (perLine.Status != PromptStatus.OK) return;
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
                    var faceLine = (Polyline)tr.GetObject(lineId, OpenMode.ForRead);
                    Point3d p1 = faceLine.GetPoint3dAt(0);
                    Point3d p2 = faceLine.GetPoint3dAt(1);
                    Vector3d axisDir = (p2 - p1).GetNormal();
                    Vector3d normalDir = new Vector3d(-axisDir.Y, axisDir.X, 0);
                    // Извлекаем точки из облаков через traverseAllPointData
                    List<Point3d> excPoints = ExtractPointsViaTraverse(tr.GetObject(excId, OpenMode.ForRead), ed, "Выработка");
                    List<Point3d> quarryPoints = ExtractPointsViaTraverse(tr.GetObject(quarryId, OpenMode.ForRead), ed, "Карьер");
                    if (excPoints == null || excPoints.Count == 0 || quarryPoints == null || quarryPoints.Count == 0)
                    {
                        ed.WriteMessage("\n[ОШИБКА] Не удалось извлечь точки. Проверьте лог выше.");
                        tr.Abort();
                        return;
                    }
                    ed.WriteMessage($"\nТочек в выработке: {excPoints.Count}, в карьере: {quarryPoints.Count}");
                    // Фильтрация и поиск минимальной высоты
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
                        if (distAlongAxis >= 0 && distAlongAxis <= length && Math.Abs(distAlongNormal) <= halfWidth)
                        {
                            pointsInZone++;
                            var quarryPt = quarryPoints
                                .Where(q => Math.Abs(q.X - excPt.X) < 0.5 && Math.Abs(q.Y - excPt.Y) < 0.5)
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
                        ed.WriteMessage("\nВ заданной зоне не найдено корректных пар точек.");
                    }
                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nОшибка: {ex.Message}");
                    tr.Abort();
                }
            }
        }
        // Извлечение точек через метод traverseAllPointData (рефлексия)
        private List<Point3d> ExtractPointsViaTraverse(DBObject dbObj, Editor ed, string name)
        {
            var points = new List<Point3d>();
            Type type = dbObj.GetType();
            // Ищем метод traverseAllPointData (может называться по-разному)
            var method = type.GetMethod("traverseAllPointData", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                // Пробуем альтернативные имена
                foreach (var mName in new[] { "TraverseAllPointData", "traverseAllPoints", "TraverseAllPoints" })
                {
                    method = type.GetMethod(mName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (method != null) break;
                }
            }
            if (method == null)
            {
                ed.WriteMessage($"\n[{name}] Метод traverseAllPointData не найден.");
                return null;
            }
            try
            {
                // Метод принимает делегат-обработчик, который вызывается для каждого буфера точек.
                // Мы создаем делегат, который собирает точки.
                // В C# это можно сделать через Expression Trees или просто через dynamic.
                // Но проще всего использовать dynamic и передать лямбду.
                // Однако, поскольку метод ожидает конкретный тип делегата, это может быть сложно.
                // Вместо этого попробуем вызвать метод без параметров, если такая перегрузка есть.
                // Или используем подход с буфером.
                
                // Упрощенный вариант: попробуем вызвать метод, который возвращает коллекцию точек.
                var result = method.Invoke(dbObj, null);
                if (result is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (item is Point3d pt) points.Add(pt);
                        else
                        {
                            var loc = item.GetType().GetProperty("Location")?.GetValue(item) ?? item.GetType().GetProperty("Point")?.GetValue(item);
                            if (loc is Point3d p) points.Add(p);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[{name}] Ошибка при вызове traverseAllPointData: {ex.Message}");
                return null;
            }
            if (points.Count > 0)
            {
                ed.WriteMessage($"\n[{name}] Успешно извлечено точек: {points.Count}");
                return points;
            }
            ed.WriteMessage($"\n[{name}] Метод найден, но точки не извлечены. Возможно, требуется другая сигнатура.");
            return null;
        }
    }
}
