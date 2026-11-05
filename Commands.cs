using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Reflection;
using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using HostMgd.Windows;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using laszip.net;

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
        private Button btnSetQuarry, btnSetExc, btnSetLine, btnCalculate, btnLog;
        private Label lblResult;
        private ObjectId _quarryId = ObjectId.Null, _excId = ObjectId.Null, _lineId = ObjectId.Null;
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
                lblResult.Text = "Обработка (см. лог)...";
                System.Windows.Forms.Application.DoEvents();

                try
                {
                    double minDist = ProcessZoneFromFiles(pprFace.Value);
                    lblResult.Text = $"РЕЗУЛЬТАТ:\nМин. расстояние:\n{minDist:F2} м.\n\n(Нажмите 'Показать лог' для деталей)";
                }
                catch (System.Exception ex)
                {
                    _log.AppendLine($"КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}");
                    lblResult.Text = "Ошибка! Смотрите лог.";
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

        private double ProcessZoneFromFiles(Point3d ptClick)
        {
            Document doc = HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
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
            // Координаты зоны 15х20
            Point3d p1 = ptOnLine + vSide * -7.5;
            Point3d p2 = ptOnLine + vSide * 7.5;
            Point3d p3 = p2 + vFwd * 20.0;
            Point3d p4 = p1 + vFwd * 20.0;

            // 1. Читаем пути к файлам
            string pathExc = GetFilePath(_excId);
            string pathQuarry = GetFilePath(_quarryId);

            _log.AppendLine($"Путь Карьера: {pathExc}");
            _log.AppendLine($"Путь Выработки: {pathExc}");

            if (pathExc == null || pathQuarry == null)
                throw new System.Exception("Не удалось прочитать путь к файлам из свойств объектов.");

            // 2. Читаем точки из файлов
            List<Point3d> qPoints = ReadPointCloudFile(pathQuarry);
            List<Point3d> ePoints = ReadPointCloudFile(pathExc);

            _log.AppendLine($"Загружено точек: Карьер={qPoints.Count}, Выработка={ePoints.Count}");

            // 3. Фильтрация и расчет
            double maxZ_exc = double.MinValue;
            foreach (var pt in ePoints)
            {
                Vector3d vec = pt - ptOnLine;
                // Только в полосе 0.5м от забоя
                if (vec.DotProduct(vFwd) >= 0 && vec.DotProduct(vFwd) <= 0.5 && Math.Abs(vec.DotProduct(vSide)) <= 7.5)
                    if (pt.Z > maxZ_exc) maxZ_exc = pt.Z;
            }

            double minZ_quarry = double.MaxValue;
            foreach (var pt in qPoints)
            {
                Vector3d vec = pt - ptOnLine;
                // В зоне 20м
                if (vec.DotProduct(vFwd) >= 0 && vec.DotProduct(vFwd) <= 20.0 && Math.Abs(vec.DotProduct(vSide)) <= 7.5)
                    if (pt.Z < minZ_quarry) minZ_quarry = pt.Z;
            }

            _log.AppendLine($"Верх выработки: {maxZ_exc:F3}, Низ карьера: {minZ_quarry:F3}");

            double result = 0;
            if (minZ_quarry > maxZ_exc) 
                result = minZ_quarry - maxZ_exc;
            
            // Ставим подпись
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                DBText text = new DBText();
                text.Position = ptOnLine + vSide * 2.0 + new Vector3d(0, 0, 1); 
                text.Height = 1.5;
                text.TextString = $"H = {result:F2} м";
                btr.AppendEntity(text);
                tr.AddNewlyCreatedDBObject(text, true);
                tr.Commit();
            }

            return result;
        }

        // Метод извлечения пути через рефлексию (ищем свойства Path, FileName, ActiveFileName и т.д.)
        private string GetFilePath(ObjectId id)
        {
            Document doc = HostMgd.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                object ent = tr.GetObject(id, OpenMode.ForRead);
                Type t = ent.GetType();
                
                // Ищем свойства, в которых может лежать путь
                string[] propNames = { "Path", "FileName", "ActiveFileName", "SourceFileName", "PointCloudFile" };
                foreach (var name in propNames)
                {
                    var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null)
                    {
                        string val = prop.GetValue(ent) as string;
                        if (!string.IsNullOrEmpty(val)) return val;
                    }
                }
                tr.Commit();
            }
            return null;
        }

        // Чтение файла LAS/XYZ
        private List<Point3d> ReadPointCloudFile(string path)
        {
            var pts = new List<Point3d>();
            if (!File.Exists(path)) throw new System.Exception($"Файл не найден: {path}");

            // Если это LAS-файл, читаем через LASzip
            if (path.EndsWith(".las", StringComparison.OrdinalIgnoreCase))
            {
                var laszip = new laszip_dll();
                bool isCompressed = false;
                if (laszip.laszip_open_reader(path, ref isCompressed) != 0) throw new System.Exception("Ошибка открытия LAS.");

                var header = laszip.header;
                long pointCount = header.number_of_point_records;

                for (long i = 0; i < pointCount; i++)
                {
                    laszip.laszip_read_point();
                    double x = laszip.point.X * header.x_scale_factor + header.x_offset;
                    double y = laszip.point.Y * header.y_scale_factor + header.y_offset;
                    double z = laszip.point.Z * header.z_scale_factor + header.z_offset;
                    pts.Add(new Point3d(x, y, z));
                }
                laszip.laszip_close_reader();
            }
            // Если это текстовый файл (XYZ, CSV, TXT)
            else
            {
                foreach (var line in File.ReadLines(path))
                {
                    var parts = line.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3 && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double x) &&
                        double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double y) &&
                        double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double z))
                    {
                        pts.Add(new Point3d(x, y, z));
                    }
                }
            }

            return pts;
        }
    }
}
