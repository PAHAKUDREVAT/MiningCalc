using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using System.Globalization;
using System.Text;
using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using HostMgd.Windows;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace MiningCalc
{
    public class Commands
    {
        private static PaletteSet? _palette;
        private static SurveyControl? _surveyControl;

        [CommandMethod("SURVEY_UI")]
        public void ShowUI()
        {
            if (_palette == null)
            {
                _palette = new PaletteSet("Маркшейдерский Калькулятор");
                _surveyControl = new SurveyControl();
                _palette.Add("Панель", _surveyControl);
            }
            _palette.Visible = true;
        }
    }

    public class SurveyControl : UserControl
    {
        private Button btnSetQuarry = null!;
        private Button btnSetExc = null!;
        private Button btnSetLine = null!;
        private Button btnCalculate = null!;
        private Button btnLog = null!;
        private Label lblResult = null!;
        
        private ObjectId _quarryId = ObjectId.Null;
        private ObjectId _excId = ObjectId.Null;
        private ObjectId _lineId = ObjectId.Null;
        private StringBuilder _log = new StringBuilder();

        public SurveyControl()
        {
            this.Size = new Size(320, 450);
            this.BackColor = Color.FromArgb(240, 240, 240);
            InitializeUI();
        }

        private void InitializeUI()
        {
            btnSetQuarry = new Button { Text = "1. Указать КАРЬЕР", Top = 10, Left = 10, Width = 280, Height = 30 };
            btnSetExc = new Button { Text = "2. Указать ВЫРАБОТКУ", Top = 50, Left = 10, Width = 280, Height = 30 };
            btnSetLine = new Button { Text = "3. Указать линию ЗАБОЯ", Top = 90, Left = 10, Width = 280, Height = 30 };
            
            btnCalculate = new Button { Text = "РАСЧЕТ (Клик по забою)", Top = 140, Left = 10, Width = 280, Height = 45, BackColor = Color.LightGreen, Font = new Font("Arial", 10, FontStyle.Bold) };
            btnLog = new Button { Text = "Показать лог", Top = 195, Left = 10, Width = 280, Height = 25, BackColor = Color.LightBlue };
            
            lblResult = new Label { Text = "Готов к работе", Top = 230, Left = 10, Width = 280, Height = 150, Font = new Font("Arial", 9, FontStyle.Regular) };

            btnSetQuarry.Click += (s, e) => SelectEntity("Выберите КАРЬЕР", ref _quarryId);
            btnSetExc.Click += (s, e) => SelectEntity("Выберите ВЫРАБОТКУ", ref _excId);
            btnSetLine.Click += (s, e) => SelectLine(ref _lineId);
            btnCalculate.Click += (s, e) => StartCalculation();
            btnLog.Click += (s, e) => ShowLogWindow();

            this.Controls.Add(btnSetQuarry); this.Controls.Add(btnSetExc); this.Controls.Add(btnSetLine);
            this.Controls.Add(btnCalculate); this.Controls.Add(btnLog); this.Controls.Add(lblResult);
        }

        private void SelectEntity(string prompt, ref ObjectId id)
        {
            Editor ed = HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
            var per = ed.GetEntity($"\n{prompt}: ");
            if (per.Status == PromptStatus.OK) id = per.ObjectId;
        }

        private void SelectLine(ref ObjectId id)
        {
            Editor ed = HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
            var peo = new PromptEntityOptions("\nВыберите линию ЗАБОЯ: ");
            peo.AddAllowedClass(typeof(Polyline), false);
            var per = ed.GetEntity(peo);
            if (per.Status == PromptStatus.OK) id = per.ObjectId;
        }

        private void StartCalculation()
        {
            if (_quarryId.IsNull || _excId.IsNull || _lineId.IsNull) 
            { 
                MessageBox.Show("Выберите все 3 объекта!"); 
                return; 
            }
            
            Editor ed = HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;

            while (true)
            {
                var pprFace = ed.GetPoint("\nУкажите точку на ЛИНИИ ЗАБОЯ (Enter для выхода): ");
                if (pprFace.Status != PromptStatus.OK) break;

                _log.Clear();
                _log.AppendLine("--- НАЧАЛО РАСЧЕТА ---");
                lblResult.Text = "Обработка...";
                System.Windows.Forms.Application.DoEvents();

                try
                {
                    double minDist = ProcessZoneViaView(pprFace.Value);
                    lblResult.Text = $"РЕЗУЛЬТАТ:\nМин. расстояние:\n{minDist:F2} м.\n\n(Нажмите 'Показать лог')";
                }
                catch (System.Exception ex)
                {
                    _log.AppendLine($"КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}");
                    lblResult.Text = "Ошибка! Смотрите лог.";
                    MessageBox.Show($"Ошибка!\n{ex.Message}", "Сбой", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ShowLogWindow()
        {
            Form logForm = new Form
            {
                Text = "Лог отладки",
                Size = new Size(600, 500),
                StartPosition = FormStartPosition.CenterScreen
            };
            TextBox txtLog = new TextBox
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Both,
                Text = _log.ToString(),
                Font = new Font("Consolas", 10),
                ReadOnly = true
            };
            logForm.Controls.Add(txtLog);
            logForm.Show();
        }

        private double ProcessZoneViaView(Point3d ptClick)
        {
            Document doc = HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            Point3d ptOnLine;
            Vector3d vFwd;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Polyline faceLine = (Polyline)tr.GetObject(_lineId, OpenMode.ForRead);
                ptOnLine = faceLine.GetClosestPointTo(ptClick, false);
                
                Vector3d tangent = faceLine.GetFirstDerivative(faceLine.GetParameterAtPoint(ptOnLine)).GetNormal();
                vFwd = new Vector3d(-tangent.Y, tangent.X, 0);

                Entity excEnt = (Entity)tr.GetObject(_excId, OpenMode.ForRead);
                Point3d excCenter = excEnt.GeometricExtents.MinPoint + (excEnt.GeometricExtents.MaxPoint - excEnt.GeometricExtents.MinPoint) / 2.0;
                if (vFwd.DotProduct(excCenter - ptOnLine) < 0) vFwd = -vFwd;

                tr.Commit();
            }

            Vector3d vSide = new Vector3d(-vFwd.Y, vFwd.X, 0);

            object oldCmdEcho = HostMgd.ApplicationServices.Application.GetSystemVariable("CMDECHO");
            HostMgd.ApplicationServices.Application.SetSystemVariable("CMDECHO", 0);

            double maxZ_exc = double.MinValue;
            double minZ_quarry = double.MaxValue;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    // === 1. ИЗВЛЕКАЕМ ВЫРАБОТКУ (УЗКАЯ ПОЛОСА ВОКРУГ ЛИНИИ) ===
                    _log.AppendLine("[1] Поиск ближайшей точки выработки к линии забоя...");
                    // Делаем рамку шириной 15м, но по длине всего от -2м до +2м от линии
                    Point3d ep1 = ptOnLine - vFwd * 2.0 + vSide * -7.5;
                    Point3d ep2 = ptOnLine - vFwd * 2.0 + vSide * 7.5;
                    Point3d ep3 = ptOnLine + vFwd * 2.0 + vSide * 7.5;
                    Point3d ep4 = ptOnLine + vFwd * 2.0 + vSide * -7.5;

                    ed.Command("_.ZOOM", "_W", ep1, ep3);
                    ed.Command("_.REGEN");
                    
                    ed.Command("_.NPC_EXTRACT_FROM_VIEW", "_N", _excId, "");
                    ObjectId excCloudId = FindNewCloud(tr);
                    if (excCloudId.IsNull) throw new System.Exception("Не удалось извлечь выработку. Возможно, облако обрезано.");
                    
                    ed.Command("_.NG_EXPLODE_POINTCLOUD", excCloudId);
                    maxZ_exc = FindClosestPointZ(ed, tr, ptOnLine);
                    _log.AppendLine($"Отметка выработки (в ближайшей точке): {maxZ_exc:F3}");

                    // === 2. ИЗВЛЕКАЕМ КАРЬЕР (ЗОНА 20М) ===
                    _log.AppendLine("[2] Поиск низа карьера в 20м зоне...");
                    Point3d qp1 = ptOnLine + vSide * -7.5;
                    Point3d qp2 = ptOnLine + vSide * 7.5;
                    Point3d qp3 = ptOnLine + vFwd * 20.0 + vSide * 7.5;
                    Point3d qp4 = ptOnLine + vFwd * 20.0 + vSide * -7.5;

                    ed.Command("_.ZOOM", "_W", qp1, qp3);
                    ed.Command("_.REGEN");
                    
                    ed.Command("_.NPC_EXTRACT_FROM_VIEW", "_N", _quarryId, "");
                    ObjectId quarryCloudId = FindNewCloud(tr);
                    if (quarryCloudId.IsNull) throw new System.Exception("Не удалось извлечь карьер.");
                    
                    ed.Command("_.NG_EXPLODE_POINTCLOUD", quarryCloudId);
                    minZ_quarry = FindMinZInBox(ed, tr, ptOnLine, vFwd, vSide, 20.0, 7.5);
                    _log.AppendLine($"Низ карьера: {minZ_quarry:F3}");

                    // === РАСЧЕТ ===
                    if (maxZ_exc == double.MinValue) throw new System.Exception("В выработке не найдено точек.");
                    if (minZ_quarry == double.MaxValue) throw new System.Exception("В карьере не найдено точек.");

                    double result = 0;
                    if (minZ_quarry > maxZ_exc) result = minZ_quarry - maxZ_exc;
                    
                    _log.AppendLine($"Расчет: {minZ_quarry:F3} - ({maxZ_exc:F3}) = {result:F3}");
                    
                    // Подпись
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    DBText text = new DBText();
                    text.Position = ptOnLine + vSide * 2.0 + new Vector3d(0, 0, 1); 
                    text.Height = 1.5;
                    text.TextString = $"H = {result:F2} м";
                    btr.AppendEntity(text);
                    tr.AddNewlyCreatedDBObject(text, true);

                    return result;
                }
                finally
                {
                    HostMgd.ApplicationServices.Application.SetSystemVariable("CMDECHO", oldCmdEcho);
                    ed.Command("_.ZOOM", "_P");
                    Cleanup(tr);
                }
            }
        }

        // Поиск Z-координаты точки, ближайшей к ptOnLine в плане (XY)
        private double FindClosestPointZ(Editor ed, Transaction tr, Point3d ptOnLine)
        {
            double bestZ = double.MinValue;
            double minDist = double.MaxValue;

            TypedValue[] tv = new TypedValue[] { new TypedValue((int)DxfCode.Start, "*POINT*") };
            PromptSelectionResult psr = ed.SelectAll(new SelectionFilter(tv));
            
            if (psr.Status == PromptStatus.OK)
            {
                foreach (SelectedObject so in psr.Value)
                {
                    var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead);
                    Point3d pos = Point3d.Origin;
                    
                    if (ent is DBPoint dbPt) pos = dbPt.Position;
                    else
                    {
                        var posProp = ent.GetType().GetProperty("Position");
                        if (posProp != null && posProp.GetValue(ent) is Point3d p) pos = p;
                    }

                    if (pos != Point3d.Origin)
                    {
                        // Расстояние только в плане (игнорируем Z)
                        double dist2D = Math.Sqrt(Math.Pow(pos.X - ptOnLine.X, 2) + Math.Pow(pos.Y - ptOnLine.Y, 2));
                        
                        if (dist2D < minDist)
                        {
                            minDist = dist2D;
                            bestZ = pos.Z;
                        }
                    }
                }
            }
            return bestZ;
        }

        private double FindMinZInBox(Editor ed, Transaction tr, Point3d ptOnLine, Vector3d vFwd, Vector3d vSide, double depth, double width)
        {
            double minZ = double.MaxValue;
            TypedValue[] tv = new TypedValue[] { new TypedValue((int)DxfCode.Start, "*POINT*") };
            PromptSelectionResult psr = ed.SelectAll(new SelectionFilter(tv));
            
            if (psr.Status == PromptStatus.OK)
            {
                foreach (SelectedObject so in psr.Value)
                {
                    var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead);
                    Point3d pos = Point3d.Origin;
                    
                    if (ent is DBPoint dbPt) pos = dbPt.Position;
                    else
                    {
                        var posProp = ent.GetType().GetProperty("Position");
                        if (posProp != null && posProp.GetValue(ent) is Point3d p) pos = p;
                    }

                    if (pos != Point3d.Origin)
                    {
                        Vector3d vec = pos - ptOnLine;
                        if (vec.DotProduct(vFwd) >= 0 && vec.DotProduct(vFwd) <= depth && Math.Abs(vec.DotProduct(vSide)) <= width)
                        {
                            if (pos.Z < minZ) minZ = pos.Z;
                        }
                    }
                }
            }
            return minZ;
        }

        private ObjectId FindNewCloud(Transaction tr)
        {
            Database db = HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Database;
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            ObjectId lastId = ObjectId.Null;
            long maxHandle = -1;
            
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent != null && ent.GetType().Name.Contains("PointCloud") && id != _quarryId && id != _excId)
                {
                    if (ent.Handle.Value > maxHandle)
                    {
                        maxHandle = ent.Handle.Value;
                        lastId = id;
                    }
                }
            }
            return lastId;
        }

        private void Cleanup(Transaction tr)
        {
            Database db = HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Database;
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                if (ent != null && (ent.GetType().Name.Contains("PointCloud") || ent.GetType().Name.Contains("POINT")) && id != _quarryId && id != _excId && id != _lineId)
                {
                    ent.Erase();
                }
            }
        }
    }
}
