using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using laszip.net;
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
                    string quarryPath = GetLasFilePath(tr.GetObject(quarryId, OpenMode.ForRead), ed, "Карьер");
                    string excPath = GetLasFilePath(tr.GetObject(excId, OpenMode.ForRead), ed, "Выработка");
                    if (quarryPath == null || excPath == null)
                    {
                        ed.WriteMessage("\n[ОШИБКА] Не удалось найти LAS-файлы для выбранных облаков точек.");
                        tr.Abort();
                        return;
                    }
                    ed.WriteMessage($"\nФайл карьера: {Path.GetFileName(quarryPath)}");
                    ed.WriteMessage($"Файл выработки: {Path.GetFileName(excPath)}");
                    List<Point3d> excPoints = ReadLasPointsInZone(excPath, centerPoint, axisDir, normalDir, 20.0, 7.5);
                    List<Point3d> quarryPoints = ReadLasPointsInZone(quarryPath, centerPoint, axisDir, normalDir, 20.0, 7.5);
                    ed.WriteMessage($"\nТочек выработки в зоне: {excPoints.Count}");
                    ed.WriteMessage($"Точек карьера в зоне: {quarryPoints.Count}");
                    if (excPoints.Count == 0 || quarryPoints.Count == 0)
                    {
                        ed.WriteMessage("\n[ПРЕДУПРЕЖДЕНИЕ] В указанной зоне не найдено точек в одном из облаков.");
                        tr.Abort();
                        return;
                    }
                    double minHeight = double.MaxValue;
                    Point3d bestExc = Point3d.Origin;
                    Point3d bestQuarry = Point3d.Origin;
                    foreach (var excPt in excPoints)
                    {
                        var quarryPt = quarryPoints
                            .Where(q => Math.Abs(q.X - excPt.X) < 1.0 && Math.Abs(q.Y - excPt.Y) < 1.0)
                            .OrderBy(q => Math.Abs(q.Z - excPt.Z))
                            .FirstOrDefault();
                        if (quarryPt != Point3d.Origin)
                        {
                            double height = excPt.Z - quarryPt.Z;
                            if (height > 0.1 && height < minHeight)
                            {
                                minHeight = height;
                                bestExc = excPt;
                                bestQuarry = quarryPt;
                            }
                        }
                    }
                    if (minHeight != double.MaxValue)
                    {
                        ed.WriteMessage($"\n--- РЕЗУЛЬТАТ ---");
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
                    ed.WriteMessage($"\nОшибка при расчёте: {ex.Message}");
                    tr.Abort();
                }
            }
        }
        // Обновлённый метод: пытается получить путь через Xref, PointCloudEx или dynamic
        private string GetLasFilePath(DBObject dbObj, Editor ed, string name)
        {
            // 1. Пытаемся получить путь через Xref (как раньше)
            try
            {
                if (dbObj is BlockReference blockRef)
                {
                    var btr = (BlockTableRecord)blockRef.Database.TransactionManager.TopTransaction.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead);
                    if (btr.IsFromExternalReference)
                    {
                        string path = btr.PathName;
                        if (!string.IsNullOrEmpty(path))
                        {
                            if (File.Exists(path)) return path;
                            string docPath = Path.GetDirectoryName(blockRef.Database.Filename);
                            if (docPath != null)
                            {
                                string fullPath = Path.Combine(docPath, path);
                                if (File.Exists(fullPath)) return fullPath;
                            }
                        }
                    }
                }
            }
            catch { /* Игнорируем ошибки Xref и идём дальше */ }
            // 2. Пытаемся привести к PointCloudEx и получить ActiveFileName / SourceFileName
            try
            {
                dynamic dynObj = dbObj;
                // Пробуем разные имена свойств, которые могут содержать путь
                string[] propertyNames = { "ActiveFileName", "SourceFileName", "FileName", "PathName" };
                foreach (var propName in propertyNames)
                {
                    try
                    {
                        string path = dynObj.GetType().GetProperty(propName)?.GetValue(dynObj) as string;
                        if (!string.IsNullOrEmpty(path))
                        {
                            if (File.Exists(path)) return path;
                            // Если путь относительный, пробуем собрать с папкой документа
                            string docPath = Path.GetDirectoryName(dbObj.Database.Filename);
                            if (docPath != null)
                            {
                                string fullPath = Path.Combine(docPath, path);
                                if (File.Exists(fullPath)) return fullPath;
                            }
                        }
                    }
                    catch { /* Свойство не подошло, пробуем следующее */ }
                }
            }
            catch { /* Dynamic доступ не удался */ }
            ed.WriteMessage($"\n[{name}] Не удалось определить путь к LAS-файлу. Убедитесь, что облако вставлено как Xref или PointCloudEx с сохранённым путём.");
            return null;
        }
        // Чтение LAS с фильтрацией "на лету" (без изменений, но с исправленными координатами)
        private List<Point3d> ReadLasPointsInZone(string filePath, Point3d center, Vector3d axis, Vector3d normal, double length, double halfWidth)
        {
            var points = new List<Point3d>();
            
            try
            {
                var laszip = new laszip_dll();
                bool isCompressed = false;
                
                if (laszip.laszip_open_reader(filePath, ref isCompressed) != 0)
                {
                    return points;
                }
                
                var header = laszip.header;
                long pointCount = header.number_of_point_records;
                
                for (long i = 0; i < pointCount; i++)
                {
                    laszip.laszip_read_point();
                    
                    // Используем поля X, Y, Z (целочисленные) и масштабируем
                    double x = laszip.point.X * header.x_scale_factor + header.x_offset;
                    double y = laszip.point.Y * header.y_scale_factor + header.y_offset;
                    double z = laszip.point.Z * header.z_scale_factor + header.z_offset;
                    
                    Point3d pt = new Point3d(x, y, z);
                    
                    Vector3d vec = pt - center;
                    double distAlongAxis = vec.DotProduct(axis);
                    double distAlongNormal = vec.DotProduct(normal);
                    if (distAlongAxis >= 0 && distAlongAxis <= length && Math.Abs(distAlongNormal) <= halfWidth)
                    {
                        points.Add(pt);
                    }
                }
                
                laszip.laszip_close_reader();
            }
            catch (System.Exception)
            {
                // Возвращаем то, что успели собрать
            }
            return points;
        }
    }
}
