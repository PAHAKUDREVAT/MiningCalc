using System;
using HostMgd.ApplicationServices;
using HostMgd.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Runtime;
using HostMgd.PointClouds;

namespace MiningCalc
{
    public class Commands
    {
        [CommandMethod("SURVEY")]
        public void RunSurvey()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            try
            {
                // 1. Выбор облака точек
                PromptEntityOptions peoCloud = new PromptEntityOptions("\nВыберите облако точек карьера: ");
                peoCloud.SetRejectMessage("\nЭто не облако точек.");
                peoCloud.AddAllowedClass(typeof(PointCloudInsertion), true);

                PromptEntityResult perCloud = ed.GetEntity(peoCloud);
                if (perCloud.Status != PromptStatus.OK) return;

                // 2. Выбор линии горизонта
                PromptEntityOptions peoLine = new PromptEntityOptions("\nВыберите линию на слое горизонта: ");
                peoLine.SetRejectMessage("\nНеобходимо выбрать полилинию.");
                peoLine.AddAllowedClass(typeof(Polyline), false);

                PromptEntityResult perLine = ed.GetEntity(peoLine);
                if (perLine.Status != PromptStatus.OK) return;

                // Выполнение
                ExecuteCalculation(doc, perCloud.ObjectId, perLine.ObjectId);
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\n[ОШИБКА] {ex.Message}");
            }
        }

        private void ExecuteCalculation(Document doc, ObjectId cloudId, ObjectId lineId)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    Polyline poly = (Polyline)tr.GetObject(lineId, OpenMode.ForRead);
                    PointCloudInsertion cloud = (PointCloudIn