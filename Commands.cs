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
        [CommandMethod("SURVEY_FAST")]
        public void RunSurveyFast()
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

                // Запрашиваем точки у пользователя
                while (true)
                {
                    var ppoFace = new PromptPointOptions("\nТочка выпуска на ЛИНИИ ЗАБОЯ (Enter для выхода): ");
                    ppoFace.AllowNone = true;
                    var pprFace = ed.GetPoint(ppoFace);
                    if (pprFace.Status != PromptStatus.OK) break;
                    Point3d ptFace = pprFace.Value;

                    var ppoInward = new PromptPointOptions("\nУкажите точку ВНУТРИ выработки (направление): ");
                    ppoInward.UseBasePoint = true;
                    ppoInward.BasePoint = ptFace;
                    var pprInward = ed.GetPoint(ppoInward);
                    if (pprInward.Status != PromptStatus.OK) break;
                    Point3d ptInward = pprInward.Value;

                    // Запускаем чистый математический расчет без команд CAD
                    ExecuteMathCalculation(doc, perQuarry.ObjectId, perExc.ObjectId, ptFace, ptInward);
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[ОШИБКА] {ex.Message}");
            }
        }

        private void ExecuteMathCalculation(Document doc, ObjectId quarryId, ObjectId excId, Point3d ptFace, Point3d ptInward)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;

            Vector3d vFwd = (ptInward - ptFace).GetNormal(); // Направление вглубь
            Vector3d vSide = new Vector3d(-vFwd.Y, vFwd.X, 0); // Перпендикуляр

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    // Читаем точки из обоих облаков в память
                    Entity excEnt = (Entity)tr.GetObject(excId, OpenMode.ForRead);
                    Entity quarryEnt = (Entity)tr.GetObject(quarryId, OpenMode.ForRead);

                    List<Point3d> excPoints = GetAllPointsReflection(excEnt);
                    List<Point3d> quarryPoints = GetAllPointsReflection(quarryEnt);

                    ed.WriteMessage($"\n[1] Загружено точек: Выработка={excPoints.Count}, Карьер={quarryPoints.Count}");

                    // --- ШАГ 1: Фиксируем верх выработки (в радиусе 2м от точки) ---
                    double fixed_roof_z = double.MinValue;
                    foreach (var pt in excPoints)
                    {
                        // Если точка в радиусе 7.5м по бокам и 2м вперед
                        Vector3d vec = pt - ptFace;
                        double dFwd = vec.DotProduct(vFwd);
                        double dSide = vec.DotProduct(vSide);

                        if (dFwd >= 0 && dFwd <= 2.0 && Math.Abs(dSide) <= 7.5)
                        {
                            if (pt.Z > fixed_roof_z) fixed_roof_z = pt.Z;
                        }
                    }

                    if (fixed_roof_z == double.MinValue)
                    {
                        ed.WriteMessage("\n[ОШИБКА] В зоне забоя не найдено точек выработки.");
                        return;
                    }

                    // --- ШАГ 2: Ищем минимальное расстояние до карьера (в интервале 20м) ---
                    double minDist = double.MaxValue;
                    bool found = false;

                    foreach (var pt in quarryPoints)
                    {
                        Vector3d vec = pt - ptFace;
                        double dFwd = vec.DotProduct(vFwd);
                        double dSide = vec.DotProduct(vSide);

                        // Зона 20м вглубь, 7.5м в стороны
                        if (dFwd >= 0 && dFwd <= 20.0 && Math.Abs(dSide) <= 7.5)
                        {
                            double dist = Math.Abs(fixed_roof_z - pt.Z);
                            if (dist < minDist)
                            {
                                minDist = dist;
                                found = true;
                            }
                        }
                    }

                    // --- ШАГ 3: Вывод результата ---
                    if (found)
                    {
                        ed.WriteMessage($"\n[2] РЕЗУЛЬТАТ: H = {minDist:F2} м. (Верх выработки: {fixed_roof_z:F2})");

                        // Ставим подпись
                        using (Transaction trTxt = db.TransactionManager.StartTransaction())
                        {
                            var btrTxt = (BlockTableRecord)trTxt.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                            Point3d textPt = ptFace + new Vector3d(2, 2, 0);
                            DBText text = new DBText();
                            text.Position = textPt;
                            text.Height = 1.5;
                            text.TextString = $"H = {minDist:F2} м";
                            btrTxt.AppendEntity(text);
                            trTxt.AddNewlyCreatedDBObject(text, true);
                            trTxt.Commit();
                        }
                    }
                    else
                    {
                        ed.WriteMessage("\n[ОШИБКА] В 20-метровой зоне не найдено точек карьера.");
                    }
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[ОШИБКА РАСЧЕТА] {ex.Message}");
                }
            }
        }

        // Метод прямого извлечения точек из облака (через allVertexIterator)
        private List<Point3d> GetAllPointsReflection(Entity ent)
        {
            var points = new List<Point3d>();
            try
            {
                // Пытаемся найти свойство allVertexIterator, которое было в вашей первой DLL
                var prop = ent.GetType().GetProperty("allVertexIterator", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (prop == null)
                {
                    // Если свойства нет, пробуем метод
                    var method = ent.GetType().GetMethod("GetAllVertexIterator", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (method != null) prop = null; // Заглушка, обработаем ниже
                }

                object iterator = null;
                if (prop != null) iterator = prop.GetValue(ent);
                else
                {
                    var method = ent.GetType().GetMethod("GetAllVertexIterator", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (method != null) iterator = method.Invoke(ent, null);
                }

                if (iterator != null)
                {
                    var nextMethod = iterator.GetType().GetMethod("Next");
                    var currentProp = iterator.GetType().GetProperty("Current");

                    while (true)
                    {
                        bool hasNext = false;
                        if (nextMethod != null)
                        {
                            var res = nextMethod.Invoke(iterator, null);
                            if (res is bool b) hasNext = b;
                            else if (res != null) hasNext = true;
                        }

                        if (!hasNext && nextMethod != null) break;

                        var current = currentProp?.GetValue(iterator);
                        if (current is Point3d pt) points.Add(pt);
                        else if (current != null)
                        {
                            // Если итератор возвращает объект, ищем в нем координаты
                            var loc = current.GetType().GetProperty("Location")?.GetValue(current);
                            if (loc is Point3d p) points.Add(p);
                        }
                    }
                }
            }
            catch
            {
                // Игнорируем, если что-то пошло не так
            }
            return points;
        }
    }
}
