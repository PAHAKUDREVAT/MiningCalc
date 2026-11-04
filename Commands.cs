using System;
using System.Collections.Generic;
using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace MiningCalc
{
    public class Commands
    {
        [CommandMethod("SURVEY_MIN")]
        public void RunSurveyMin()
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

                    ExecuteCalculation(doc, perQuarry.ObjectId, perExc.ObjectId, ptFace, ptInward);
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[ОШИБКА] {ex.Message}");
            }
        }

        private void ExecuteCalculation(Document doc, ObjectId quarryId, ObjectId excId, Point3d ptFace, Point3d ptInward)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;

            Vector3d vFwd = (ptInward - ptFace).GetNormal();
            Vector3d vSide = new Vector3d(-vFwd.Y, vFwd.X, 0);

            // Рамка для выработки (15х1)
            Point3d p1_s = ptFace + vSide * -7.5;
            Point3d p2_s = ptFace + vSide * 7.5;
            Point3d p3_s = p2_s + vFwd * 1.0;
            Point3d p4_s = p1_s + vFwd * 1.0;

            // Рамка для карьера (15х20)
            Point3d p1_l = ptFace + vSide * -7.5;
            Point3d p2_l = ptFace + vSide * 7.5;
            Point3d p3_l = p2_l + vFwd * 20.0;
            Point3d p4_l = p1_l + vFwd * 20.0;

            ObjectId newExcId = ObjectId.Null;
            ObjectId newQuarryId = ObjectId.Null;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    Polyline pline_s = CreatePolyline(p1_s, p2_s, p3_s, p4_s);
                    btr.AppendEntity(pline_s);
                    tr.AddNewlyCreatedDBObject(pline_s, true);

                    Polyline pline_l = CreatePolyline(p1_l, p2_l, p3_l, p4_l);
                    btr.AppendEntity(pline_l);
                    tr.AddNewlyCreatedDBObject(pline_l, true);
                    
                    tr.Commit();

                    // 1. Обрезка и ИЗВЛЕЧЕНИЕ в новые объекты
                    ed.WriteMessage("\n[1] Обрезка и извлечение данных...");
                    
                    // Обрезаем выработку
                    ed.Command("_.NPC_CROP", excId, "", pline_s.ObjectId, "");
                    ed.Command("_.NPC_EXTRACT_FROM_VIEW", excId, "");
                    newExcId = GetLastCreatedEntityId(db);

                    // Обрезаем карьер
                    ed.Command("_.NPC_CROP", quarryId, "", pline_l.ObjectId, "");
                    ed.Command("_.NPC_EXTRACT_FROM_VIEW", quarryId, "");
                    newQuarryId = GetLastCreatedEntityId(db);

                    // Удаляем временные рамки
                    using (Transaction trDel = db.TransactionManager.StartTransaction())
                    {
                        ((Entity)trDel.GetObject(pline_s.ObjectId, OpenMode.ForWrite)).Erase();
                        ((Entity)trDel.GetObject(pline_l.ObjectId, OpenMode.ForWrite)).Erase();
                        trDel.Commit();
                    }

                    // 2. Чтение габаритов ИЗВЛЕЧЕННЫХ (маленьких) облаков
                    double fixed_roof_z = 0;
                    double z_q_min = 0, z_q_max = 0;

                    if (newExcId != ObjectId.Null)
                        fixed_roof_z = GetMaxZ(newExcId, db);
                    
                    if (newQuarryId != ObjectId.Null)
                    {
                        z_q_min = GetMinZ(newQuarryId, db);
                        z_q_max = GetMaxZ(newQuarryId, db);
                    }

                    // 3. Расчет
                    double minDist = 0;
                    if (newExcId != ObjectId.Null && newQuarryId != ObjectId.Null)
                    {
                        if (fixed_roof_z >= z_q_min && fixed_roof_z <= z_q_max)
                            minDist = 0.0;
                        else
                        {
                            double dist1 = Math.Abs(fixed_roof_z - z_q_min);
                            double dist2 = Math.Abs(fixed_roof_z - z_q_max);
                            minDist = Math.Min(dist1, dist2);
                        }
                    }

                    ed.WriteMessage($"\n[2] РЕЗУЛЬТАТ:");
                    ed.WriteMessage($"\n    Верх выработки: {fixed_roof_z:F2}");
                    ed.WriteMessage($"\n    Карьер от: {z_q_min:F2} до {z_q_max:F2}");
                    ed.WriteMessage($"\n    Минимальное расстояние: {minDist:F2} м.");

                    // 4. Подпись
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
                        
                        // Удаляем временные извлеченные облака
                        if (newExcId != ObjectId.Null)
                            ((Entity)trTxt.GetObject(newExcId, OpenMode.ForWrite)).Erase();
                        if (newQuarryId != ObjectId.Null)
                            ((Entity)trTxt.GetObject(newQuarryId, OpenMode.ForWrite)).Erase();
                            
                        trTxt.Commit();
                    }
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[ОШИБКА РАСЧЕТА] {ex.Message}");
                }
            }
        }

        // Получение ID последнего созданного объекта
        private ObjectId GetLastCreatedEntityId(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                
                // В .NET нет прямого entlast, поэтому перебираем и ищем самый новый
                ObjectId lastId = ObjectId.Null;
                long maxHandle = -1;
                
                foreach (ObjectId id in btr)
                {
                    try
                    {
                        var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        if (ent.Handle.Value > maxHandle)
                        {
                            maxHandle = ent.Handle.Value;
                            lastId = id;
                        }
                    }
                    catch { }
                }
                return lastId;
            }
        }

        private Polyline CreatePolyline(Point3d p1, Point3d p2, Point3d p3, Point3d p4)
        {
            Polyline pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(p1.X, p1.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(p2.X, p2.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(p3.X, p3.Y), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(p4.X, p4.Y), 0, 0, 0);
            pl.Closed = true;
            return pl;
        }

        private double GetMaxZ(ObjectId id, Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                var ext = ent.GeometricExtents;
                return Math.Max(ext.MinPoint.Z, ext.MaxPoint.Z);
            }
        }

        private double GetMinZ(ObjectId id, Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                var ext = ent.GeometricExtents;
                return Math.Min(ext.MinPoint.Z, ext.MaxPoint.Z);
            }
        }
    }
}
