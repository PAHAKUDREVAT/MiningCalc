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
        private static PaletteSet _palette;
        private static SurveyControl _surveyControl;

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
        private Button btnSetQuarry, btnSetExc, btnSetLine, btnCalculate, btnLog, btnCleanup;
        private Label lblResult;
        private ObjectId _quarryId = ObjectId.Null, _excId = ObjectId.Null, _lineId = ObjectId.Null;
        private StringBuilder _log = new StringBuilder();

        public SurveyControl()
        {
            this.Size = new Size(320, 480);
            this.BackColor = Color.FromArgb(240, 240, 240);
            InitializeUI();
        }

        private void InitializeUI()
        {
            btnSetQuarry = new Button { Text = "1. Указать КАРЬЕР", Top = 10, Left = 10, Width = 280, Height = 30 };
            btnSetExc = new Button { Text = "2. Указать ВЫРАБОТКУ", Top = 50, Left = 10, Width = 280, Height = 30 };
            btnSetLine = new Button { Text = "3. Указать линию ЗАБОЯ", Top = 90, Left = 10, Width = 280, Height = 30 };
            
            btnCalculate = new Button { Text = "РАСЧЕТ (Клик по забою)", Top = 140, Left = 10, Width = 280, Height = 45, BackColor = Color.LightGreen, Font = new Font("Arial", 10, FontStyle.Bold) };
            
            btnLog = new Button { Text = "Показать лог", Top = 195, Left = 10, Width = 135, Height = 25, BackColor = Color.LightBlue };
            btnCleanup = new Button { Text = "Очистить мусор", Top = 195, Left = 155, Width = 135, Height = 25, BackColor = Color.LightCoral };
            
            lblResult = new Label { Text = "Готов к работе", Top = 230, Left = 10, Width = 280, Height = 150, Font = new Font("Arial", 9, FontStyle.Regular) };

            btnSetQuarry.Click += (s, e) => SelectEntity("Выберите КАРЬЕР", ref _quarryId);
            btnSetExc.Click += (s, e) => SelectEntity("Выберите ВЫРАБОТКУ", ref _excId);
            btnSetLine.Click += (s, e) => SelectLine(ref _lineId);
            btnCalculate.Click += (s, e) => StartCalculation();
            btnLog.Click += (s, e) => ShowLogWindow();
            btnCleanup.Click += (s, e) => EmergencyCleanup();

            this.Controls.Add(btnSetQuarry); this.Controls.Add(btnSetExc); this.Controls.Add(btnSetLine);
            this.Controls.Add(btnCalculate); this.Controls.Add(btnLog); this.Controls.Add(btnCleanup); this.Controls.Add(lblResult);
        }

        private void SelectEntity(string prompt, ref ObjectId id)
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
            var per = ed.GetEntity($"\n{prompt}: ");
            if (per.Status == PromptStatus.OK) id = per.ObjectId;
        }

        private void SelectLine(ref ObjectId id)
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
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
            
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;

            while (true)
            {
                var pprFace = ed.GetPoint("\nУкажите точку на ЛИНИИ ЗАБОЯ (Enter для выхода): ");
                if (pprFace.Status != PromptStatus.OK) break;

                _log.Clear();
                _log.AppendLine("--- НАЧАЛО РАСЧЕТА ---");
                lblResult.Text = "Обработка (см. лог)...";
                Application.DoEvents();

                try
                {
                    double minDist = ProcessZoneWithFallback(pprFace.Value);
                    lblResult.Text = $"РЕЗУЛЬТАТ:\nМин. расстояние:\n{minDist:F2} м.\n\n(Нажмите 'Показать лог' для деталей)";
                }
                catch (Exception ex)
                {
                    _log.AppendLine($"КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}");
                    lblResult.Text = "Ошибка! Смотрите лог.";
                    MessageBox.Show($"Ошибка!\n\n{ex.Message}\n\nНажмите 'Показать лог' на панели, чтобы увидеть подробности.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private double ProcessZoneWithFallback(Point3d ptClick)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            Point3d ptOnLine;
            Vector3d vFwd;

            // 1. Определение направления (авто-борта)
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    Polyline faceLine = (Polyline)tr.GetObject(_lineId, OpenMode.ForRead);
                    ptOnLine = faceLine.GetClosestPointTo(ptClick, false);
                    
                    Vector3d tangent = faceLine.GetFirstDerivative(faceLine.GetParameterAtPoint(ptOnLine)).GetNormal();
                    Vector3d roughNormal = new Vector3d(-tangent.Y, tangent.X, 0);

                    Entity excEnt = (Entity)tr.GetObject(_excId, OpenMode.ForRead);
                    Point3d excCenter = excEnt.GeometricExtents.MinPoint + (excEnt.GeometricExtents.MaxPoint - excEnt.GeometricExtents.MinPoint) / 2.0;
                    if (roughNormal.DotProduct(excCenter - ptOnLine) < 0) roughNormal = -roughNormal;

                    // Временная рамка для поиска бортов
                    Point3d searchP1 = ptOnLine + new Vector3d(-tangent.Y, tangent.X, 0) * -15 + roughNormal * -5;
                    Point3d searchP2 = ptOnLine + new Vector3d(-tangent.Y, tangent.X, 0) * 15 + roughNormal * -5;
                    Point3d searchP3 = searchP2 + roughNormal * 30;
                    Point3d searchP4 = searchP1 + roughNormal * 30;

                    Polyline searchBox = new Polyline();
                    searchBox.AddVertexAt(0, new Point2d(searchP1.X, searchP1.Y), 0, 0, 0);
                    searchBox.AddVertexAt(1, new Point2d(searchP2.X, searchP2.Y), 0, 0, 0);
                    searchBox.AddVertexAt(2, new Point2d(searchP3.X, searchP3.Y), 0, 0, 0);
                    searchBox.AddVertexAt(3, new Point2d(searchP4.X, searchP4.Y), 0, 0, 0);
                    searchBox.Closed = true;
                    
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    btr.AppendEntity(searchBox);
                    tr.AddNewlyCreatedDBObject(searchBox, true);

                    ed.Command("_.ZOOM", "_W", searchP1, searchP3);
                    ed.SetImpliedSelection(new ObjectId[] { _excId });
                    ed.Command("_.NPC_CLIP_FENCE", searchP1, searchP2, searchP3, searchP4);
                    ed.SetImpliedSelection(new ObjectId[] { _excId });
                    ed.Command("_.NPC_EXTRACT_FROM_VIEW", "_N");
                    
                    ObjectId tempCloudId = FindLastCloud(tr);
                    searchBox.Erase();

                    if (tempCloudId.IsNull)
                        throw new Exception("Не удалось извлечь кусок выработки для анализа бортов.");

                    Entity tempCloud = (Entity)tr.GetObject(tempCloudId, OpenMode.ForRead);
                    Extents3d ext = tempCloud.GeometricExtents;
                    tempCloud.Erase();

                    if (Math.Abs(ext.MaxPoint.X - ext.MinPoint.X) > Math.Abs(ext.MaxPoint.Y - ext.MinPoint.Y))
                        vFwd = new Vector3d(ext.MaxPoint.X - ext.MinPoint.X, 0, 0).GetNormal();
                    else
                        vFwd = new Vector3d(0, ext.MaxPoint.Y - ext.MinPoint.Y, 0).GetNormal();

                    if (vFwd.DotProduct(excCenter - ptOnLine) < 0) vFwd = -vFwd;
                    tr.Commit();
                    _log.AppendLine("Направление бортов определено успешно.");
                }
                catch (Exception ex)
                {
                    tr.Abort();
                    _log.AppendLine($"ОШИБКА автоопределения бортов: {ex.Message}. Использую нормаль к забою.");
                    Polyline faceLine = (Polyline)tr.GetObject(_lineId, OpenMode.ForRead);
                    ptOnLine = faceLine.GetClosestPointTo(ptClick, false);
                    Vector3d tangent = faceLine.GetFirstDerivative(faceLine.GetParameterAtPoint(ptOnLine)).GetNormal();
                    vFwd = new Vector3d(-tangent.Y, tangent.X, 0);
                }
            }

            Vector3d vSide = new Vector3d(-vFwd.Y, vFwd.X, 0);
            Point3d p1 = ptOnLine + vSide * -7.5;
            Point3d p2 = ptOnLine + vSide * 7.5;
            Point3d p3 = p2 + vFwd * 20.0;
            Point3d p4 = p1 + vFwd * 20.0;

            // 2. Обрезка и расчет с фолбэками
            object oldCmdEcho = Application.GetSystemVariable("CMDECHO");
            Application.SetSystemVariable("CMDECHO", 0);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    ed.Command("_.ZOOM", "_W", p1, p3);

                    // --- ВЕРХ ВЫРАБОТКИ ---
                    ed.SetImpliedSelection(new ObjectId[] { _excId });
                    ed.Command("_.NPC_CLIP_FENCE", p1, p2, p3, p4);
                    double maxZ_exc = GetMaxZWithFallback(ed, tr, ptOnLine, vFwd, vSide, 0.5, 7.5);

                    // --- НИЗ КАРЬЕРА ---
                    ed.SetImpliedSelection(new ObjectId[] { _quarryId });
                    ed.Command("_.NPC_CLIP_FENCE", p1, p2, p3, p4);
                    double minZ_quarry = GetMinZWithFallback(ed, tr, ptOnLine, vFwd, vSide, 20.0, 7.5);

                    _log.AppendLine($"Значения: Верх.выработки = {maxZ_exc:F3}, Низ.карьера = {minZ_quarry:F3}");

                    double result = 0;
                    if (minZ_quarry > maxZ_exc) 
                    {
                        result = minZ_quarry - maxZ_exc;
                        _log.AppendLine($"[Расчет] {minZ_quarry:F3} - ({maxZ_exc:F3}) = {result:F3}");
                    }
                    else 
                    {
                        _log.AppendLine("[Расчет] Карьер ниже выработки или равен ей. Результат = 0.");
                    }

                    BlockTableRecord btrTxt = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    DBText text = new DBText();
                    text.Position = ptOnLine + vSide * 2.0 + new Vector3d(0, 0, 1); 
                    text.Height = 1.5;
                    text.TextString = $"H = {result:F2} м";
                    btrTxt.AppendEntity(text);
                    tr.AddNewlyCreatedDBObject(text, true);

                    return result;
                }
                finally
                {
                    Application.SetSystemVariable("CMDECHO", oldCmdEcho);
                    ed.Command("_.ZOOM", "_P");
                    CleanupTempObjects(db);
                }
            }
        }

        private double GetMaxZWithFallback(Editor ed, Transaction tr, Point3d ptOnLine, Vector3d vFwd, Vector3d vSide, double depth, double width)
        {
            try
            {
                double res = TryExplode(ed, tr, ptOnLine, vFwd, vSide, depth, width, true);
                if (res != double.MinValue) { _log.AppendLine("[Верх] Метод А (Взрыв): УСПЕХ."); return res; }
            }
            catch (Exception ex) { _log.AppendLine($"[Верх] Метод А: ОШИБКА - {ex.Message}"); }

            try
            {
                double res = TryExport(ed, tr, ptOnLine, vFwd, vSide, depth, width, true);
                if (res != double.MinValue) { _log.AppendLine("[Верх] Метод Б (Экспорт): УСПЕХ."); return res; }
            }
            catch (Exception ex) { _log.AppendLine($"[Верх] Метод Б: ОШИБКА - {ex.Message}"); }

            try
            {
                double res = TryBBox(ed, tr, ptOnLine, vFwd, vSide, depth, width, true);
                if (res != double.MinValue) { _log.AppendLine("[Верх] Метод В (Габариты): УСПЕХ."); return res; }
            }
            catch (Exception ex) { _log.AppendLine($"[Верх] Метод В: ОШИБКА - {ex.Message}"); }

            throw new Exception("Все методы поиска ВЕРХА провалились.");
        }

        private double GetMinZWithFallback(Editor ed, Transaction tr, Point3d ptOnLine, Vector3d vFwd, Vector3d vSide, double depth, double width)
        {
            try
            {
                double res = TryExplode(ed, tr, ptOnLine, vFwd, vSide, depth, width, false);
                if (res != double.MaxValue) { _log.AppendLine("[Низ] Метод А (Взрыв): УСПЕХ."); return res; }
            }
            catch (Exception ex) { _log.AppendLine($"[Низ] Метод А: ОШИБКА - {ex.Message}"); }

            try
            {
                double res = TryExport(ed, tr, ptOnLine, vFwd, vSide, depth, width, false);
                if (res != double.MaxValue) { _log.AppendLine("[Низ] Метод Б (Экспорт): УСПЕХ."); return res; }
            }
            catch (Exception ex) { _log.AppendLine($"[Низ] Метод Б: ОШИБКА - {ex.Message}"); }

            try
            {
                double res = TryBBox(ed, tr, ptOnLine, vFwd, vSide, depth, width, false);
                if (res != double.MaxValue) { _log.AppendLine("[Низ] Метод В (Габариты): УСПЕХ."); return res; }
            }
            catch (Exception ex) { _log.AppendLine($"[Низ] Метод В: ОШИБКА - {ex.Message}"); }

            throw new Exception("Все методы поиска НИЗА провалились.");
        }

        private double TryExplode(Editor ed, Transaction tr, Point3d ptOnLine, Vector3d vFwd, Vector3d vSide, double depth, double width, bool findMax)
        {
            ObjectId targetId = findMax ? _excId : _quarryId;
            ed.SetImpliedSelection(new ObjectId[] { targetId });
            ed.Command("_.NPC_EXTRACT_FROM_VIEW", "_N");
            ObjectId cloudId = FindLastCloud(tr);
            if (cloudId.IsNull) throw new Exception("Извлечение не создало облако.");

            ed.Command("_.NG_EXPLODE_POINTCLOUD", cloudId);
            
            double result = findMax ? double.MinValue : double.MaxValue;
            TypedValue[] tv = new TypedValue[] { new TypedValue((int)DxfCode.Start, "POINT") };
            PromptSelectionResult psr = ed.SelectAll(new SelectionFilter(tv));
            
            if (psr.Status == PromptStatus.OK)
            {
                foreach (SelectedObject so in psr.Value)
                {
                    var pt = tr.GetObject(so.ObjectId, OpenMode.ForRead) as DBPoint;
                    if (pt != null)
                    {
                        Vector3d vec = pt.Position - ptOnLine;
                        if (vec.DotProduct(vFwd) >= 0 && vec.DotProduct(vFwd) <= depth && Math.Abs(vec.DotProduct(vSide)) <= width)
                        {
                            if (findMax && pt.Position.Z > result) result = pt.Position.Z;
                            if (!findMax && pt.Position.Z < result) result = pt.Position.Z;
                        }
                    }
                }
                foreach (SelectedObject so in psr.Value)
                    ((Entity)tr.GetObject(so.ObjectId, OpenMode.ForWrite)).Erase();
            }
            if (!cloudId.IsNull) ((Entity)tr.GetObject(cloudId, OpenMode.ForWrite)).Erase();
            return result;
        }

        private double TryExport(Editor ed, Transaction tr, Point3d ptOnLine, Vector3d vFwd, Vector3d vSide, double depth, double width, bool findMax)
        {
            ObjectId targetId = findMax ? _excId : _quarryId;
            string file = Path.Combine(Path.GetTempPath(), "temp_export.xyz");
            if (File.Exists(file)) File.Delete(file);

            ed.SetImpliedSelection(new ObjectId[] { targetId });
            ed.Command("_.-NPC_EXPORT", "", file);
            if (!File.Exists(file)) throw new Exception("Файл экспорта не создан.");

            double result = findMax ? double.MinValue : double.MaxValue;
            foreach (var line in File.ReadLines(file))
            {
                var parts = line.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double x) &&
                    double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double y) &&
                    double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double z))
                {
                    Point3d pt = new Point3d(x, y, z);
                    Vector3d vec = pt - ptOnLine;
                    if (vec.DotProduct(vFwd) >= 0 && vec.DotProduct(vFwd) <= depth && Math.Abs(vec.DotProduct(vSide)) <= width)
                    {
                        if (findMax && pt.Z > result) result = pt.Z;
                        if (!findMax && pt.Z < result) result = pt.Z;
                    }
                }
            }
            File.Delete(file);
            return result;
        }

        private double TryBBox(Editor ed, Transaction tr, Point3d ptOnLine, Vector3d vFwd, Vector3d vSide, double depth, double width, bool findMax)
        {
            ObjectId targetId = findMax ? _excId : _quarryId;
            ed.SetImpliedSelection(new ObjectId[] { targetId });
            ed.Command("_.NPC_EXTRACT_FROM_VIEW", "_N");
            ObjectId cloudId = FindLastCloud(tr);
            if (cloudId.IsNull) throw new Exception("Извлечение не создало облако.");

            var ext = ((Entity)tr.GetObject(cloudId, OpenMode.ForRead)).GeometricExtents;
            ((Entity)tr.GetObject(cloudId, OpenMode.ForWrite)).Erase();

            return findMax ? Math.Max(ext.MinPoint.Z, ext.MaxPoint.Z) : Math.Min(ext.MinPoint.Z, ext.MaxPoint.Z);
        }

        private ObjectId FindLastCloud(Transaction tr)
        {
            Database db = Application.DocumentManager.MdiActiveDocument.Database;
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            ObjectId lastId = ObjectId.Null;
            long maxHandle = -1;
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent != null && ent.GetType().Name.Contains("PointCloud") && ent.Handle.Value > maxHandle)
                {
                    maxHandle = ent.Handle.Value;
                    lastId = id;
                }
            }
            return lastId;
        }

        private void CleanupTempObjects(Database db)
        {
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    foreach (ObjectId id in btr)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (ent != null && (ent.GetType().Name.Contains("PointCloud") || ent is DBPoint) && id != _quarryId && id != _excId && id != _lineId)
                            ent.Erase();
                    }
                    tr.Commit();
                }
            }
            catch { }
        }

        private void EmergencyCleanup()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    int count = 0;
                    foreach (ObjectId id in btr)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        if (ent != null && (ent is DBPoint || (ent.GetType().Name.Contains("PointCloud") && id != _quarryId && id != _excId)))
                        {
                            ent.Erase();
                            count++;
                        }
                    }
                    tr.Commit();
                    MessageBox.Show($"Чертеж очищен!\nУдалено мусорных объектов: {count}", "Очистка", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex) { MessageBox.Show("Ошибка очистки: " + ex.Message); }
        }
    }
}
