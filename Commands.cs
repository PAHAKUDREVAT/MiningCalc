using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using laszip.net; // Библиотека для чтения LAS
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
                // 1. Облако карьера
                var peoQuarry = new PromptEntityOptions("\nВыберите облако точек КАРЬЕРА (внешнюю ссылку Xref): ");
                var perQuarry = ed.GetEntity(peoQuarry);
                if (perQuarry.Status != PromptStatus.OK) return;
                // 2. Облако выработки
                var peoExc = new PromptEntityOptions("\nВыберите облако точек ВЫРАБОТКИ (внешнюю ссылку Xref): ");
                var perExc = ed.GetEntity(peoExc);
                if (perExc.Status != PromptStatus.OK) return;
                // 3. Линия забоя
                var peoLine = new PromptEntityOptions("\nВыберите линию ЗАБОЯ: ");
                peoLine.AddAllowedClass(typeof(Polyline), false);
                var perLine = ed.GetEntity(peoLine);
                if (perLine.Status != PromptStatus.OK) return;
                // 4. Точка выпуска
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
                    // 1. Определяем ось забоя
                    var faceLine = (Polyline)tr.GetObject(lineId, OpenMode.ForRead);
                    Point3d p1 = faceLine.GetPoint3dAt(0);
                    Point3d p2 = faceLine.GetPoint3dAt(1);
                    Vector3d axisDir = (p2 - p1).GetNormal();
                    Vector3d normalDir = new Vector3d(-axisDir.Y, axisDir.X, 0); // Перпендикуляр
                    // 2. Получаем пути к LAS-файлам через внешние ссылки
                    string quarryPath = GetLasFilePath(tr.GetObject(quarryId, OpenMode.ForRead), ed, "Карьер");
                    string excPath = GetLasFilePath(tr.GetObject(excId, OpenMode.ForRead), ed, "Выработка");
                    if (quarryPath == null || excPath == null)
                    {
                        ed.WriteMessage("\n[ОШИБКА] Не удалось найти LAS-файлы. Убедитесь, что облака вставлены как Xref.");
                        tr.Abort();
                        return;
                    }
                    ed.WriteMessage($"\nФайл карьера: {Path.GetFileName(quarryPath)}");
                    ed.WriteMessage($"Файл выработки: {Path.GetFileName(excPath)}");
                    // 3. Читаем точки из LAS, сразу отсекая всё, что не попадает в зону 15х20 м.
                    // Это защищает память от переполнения.
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
                    // 4. Поиск минимальной высоты (мощности)
                    double minHeight = double.MaxValue;
                    SurveyResult bestResult = null;
                    foreach (var excPt in excPoints)
                    {
                        // Ищем ближайшую точку карьера по координатам X и Y (допуск 1 метр)
                        var quarryPt = quarryPoints
                            .Where(q => Math.Abs(q.X - excPt.X) < 1.0 && Math.Abs(q.Y - excPt.Y) < 1.0)
                            .OrderBy(q => Math.Abs(q.Z - excPt.Z))
                            .FirstOrDefault();
                        if (quarryPt != Point3d.Origin)
                        {
                            double height = excPt.Z - quarryPt.Z;
                            
                            // Ищем САМУЮ МАЛУЮ положительную высоту (чтобы исключить артефакты)
                            if (height > 0.1 && height < minHeight) 
                            {
                                minHeight = height;
                                bestResult = new SurveyResult
                                {
                                    MinHeight = height,
                                    ExcX = excPt.X, ExcY = excPt.Y, ExcZ = excPt.Z,
                                    QuarryX = quarryPt.X, QuarryY = quarryPt.Y, QuarryZ = quarryPt.Z
                                };
                            }
                        }
                    }
                    // 5. Вывод результатов
                    if (bestResult != null)
                    {
                        ed.WriteMessage($"\n--- РЕЗУЛЬТАТ ---");
                        ed.WriteMessage($"\nМинимальная мощность (высота): {bestResult.MinHeight:F3} м.");
                        ed.WriteMessage($"\nТочка верха выработки: X={bestResult.ExcX:F2}, Y={bestResult.ExcY:F2}, Z={bestResult.ExcZ:F3}");
                        ed.WriteMessage($"\nТочка низа карьера:     X={bestResult.QuarryX:F2}, Y={bestResult.QuarryY:F2}, Z={bestResult.QuarryZ:F3}");
                    }
                    else
                    {
                        ed.WriteMessage("\nВ заданной зоне не найдено корректных пар точек (выработка строго над карьером).");
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
        // Получение пути к файлу из Xref
        private string GetLasFilePath(DBObject dbObj, Editor ed, string name)
        {
            try
            {
                if (dbObj is BlockReference blockRef)
                {
                    var btr = (BlockTableRecord)blockRef.Database.TransactionManager.TopTransaction.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead);
                    if (btr.IsFromExternalReference)
                    {
                        string path = btr.PathName;
                        if (File.Exists(path)) return path;
                        
                        // Пытаемся собрать полный путь (если путь относительный)
                        string docPath = Path.GetDirectoryName(blockRef.Database.Filename);
                        string fullPath = Path.Combine(docPath, path);
                        if (File.Exists(fullPath)) return fullPath;
                    }
                }
                
                ed.WriteMessage($"\n[{name}] Объект не является Xref или файл не найден.");
                return null;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[{name}] Ошибка пути: {ex.Message}");
                return null;
            }
        }
        // Чтение LAS с фильтрацией "на лету"
        private List<Point3d> ReadLasPointsInZone(string filePath, Point3d center, Vector3d axis, Vector3d normal, double length, double halfWidth)
        {
            var points = new List<Point3d>();
            
            try
            {
                var laszip = new laszip_dll();
                bool isCompressed = false;
                
                // Открываем файл
                if (laszip.laszip_open_reader(filePath, ref isCompressed) != 0)
                {
                    return points; // Не удалось открыть
                }
                
                var header = laszip.header;
                long pointCount = header.number_of_point_records;
                
                // Читаем точки
                for (long i = 0; i < pointCount; i++)
                {
                    laszip.laszip_read_point();
                    
                    // Применяем масштаб и смещение
                    double x = laszip.point.X * header.x_scale_factor + header.x_offset;
                    double y = laszip.point.Y * header.y_scale_factor + header.y_offset;
                    double z = laszip.point.Z * header.z_scale_factor + header.z_offset;
                    
                    Point3d pt = new Point3d(x, y, z);
                    
                    // Проверка попадания в прямоугольную зону
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
                // В случае ошибки чтения возвращаем то, что успели собрать
            }
            return points;
        }
    }
}
