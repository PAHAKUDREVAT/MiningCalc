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
                    var excEntity = tr.GetObject(excId, OpenMode.ForRead);
                    var quarryEntity = tr.GetObject(quarryId, OpenMode.ForRead);
                    // Пытаемся извлечь точки через рефлексию
                    List<Point3d> excPoints = ExtractPointsViaReflection(excEntity, ed, "Выработка");
                    List<Point3d> quarryPoints = ExtractPointsViaReflection(quarryEntity, ed, "Карьер");
                    if (excPoints == null || quarryPoints == null || excPoints.Count == 0 || quarryPoints.Count == 0)
                    {
                        ed.WriteMessage("\n[ПРЕДУПРЕЖДЕНИЕ] Не удалось автоматически извлечь точки.");
                        ed.WriteMessage("\nАльтернатива: используйте команду _POINTCLOUDEXTRACT для выгрузки в файл и доработайте плагин для чтения файла.");
                        tr.Abort();
                        return;
                    }
                    ed.WriteMessage($"\nТочек в выработке: {excPoints.Count}, в карьере: {quarryPoints.Count}");
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
        // Попытка извлечь точки через рефлексию: ищем методы, возвращающие коллекции
        private List<Point3d> ExtractPointsViaReflection(DBObject dbObj, Editor ed, string cloudName)
        {
            var result = new List<Point3d>();
            Type type = dbObj.GetType();
            // Ищем все методы, которые могут возвращать точки
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var method in methods)
            {
                // Пропускаем методы с параметрами (нам нужны геттеры)
                if (method.GetParameters().Length > 0) continue;
                string name = method.Name.ToLower();
                if (!name.Contains("point") && !name.Contains("vertex") && !name.Contains("data")) continue;
                try
                {
                    var invokeResult = method.Invoke(dbObj, null);
                    if (invokeResult is System.Collections.IEnumerable enumerable && !(invokeResult is string))
                    {
                        var tempList = new List<Point3d>();
                        foreach (var item in enumerable)
                        {
                            if (item is Point3d p)
                            {
                                tempList.Add(p);
                            }
                            else
                            {
                                // Пытаемся извлечь координаты из item через рефлексию (свойство Location или подобное)
                                var locProp = item.GetType().GetProperty("Location") ?? item.GetType().GetProperty("LocationPoint");
                                if (locProp != null)
                                {
                                    var locValue = locProp.GetValue(item);
                                    if (locValue is Point3d lp) tempList.Add(lp);
                                }
                            }
                        }
                        if (tempList.Count > 0)
                        {
                            ed.WriteMessage($"\n[{cloudName}] Точки извлечены через метод '{method.Name}' (найдено: {tempList.Count})");
                            return tempList;
                        }
                    }
                }
                catch
                {
                    // Игнорируем ошибки при вызове неподходящих методов
                }
            }
            ed.WriteMessage($"\n[{cloudName}] Не удалось найти подходящий метод для извлечения точек.");
            return null;
        }
    }
}
