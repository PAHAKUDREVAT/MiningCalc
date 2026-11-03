using System.Collections.Generic;

namespace MiningCalc
{
    public class Point3DModel
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public class ExcavationResult
    {
        public double RoofZ { get; set; }    // Отметка кровли
        public double FloorZ { get; set; }   // Отметка подошвы
        public double VerticalDistance { get; set; } // Вертикальная мощность
        public string Type { get; set; } = string.Empty; // Тип выработки
        public List<Point3DModel> Points { get; set; } = new List<Point3DModel>();
    }
}