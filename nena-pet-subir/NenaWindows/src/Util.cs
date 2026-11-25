using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NenaPet
{
    // Muelle amortiguado: da peso y rebote a todos los movimientos.
    public class Spring
    {
        public double K, C, X, V;
        public Spring(double k, double c) { K = k; C = c; X = 0; V = 0; }
        public Spring(double k, double c, double x) { K = k; C = c; X = x; V = 0; }
        public double Step(double dt, double target)
        {
            int n = Math.Max(1, (int)Math.Ceiling(dt / 0.005));
            double h = dt / n;
            for (int i = 0; i < n; i++) { V += (K * (target - X) - C * V) * h; X += V * h; }
            return X;
        }
        public void Set(double x) { X = x; V = 0; }
    }

    // Transformación editable (traslación, giro y escala con pivote).
    public class Xf
    {
        public MatrixTransform T = new MatrixTransform();
        public void Set(double tx, double ty, double rot, double sx, double sy, double px, double py)
        {
            Matrix m = Matrix.Identity;
            m.Translate(-px, -py);
            m.Scale(sx, sy);
            m.Rotate(rot);
            m.Translate(px + tx, py + ty);
            T.Matrix = m;
        }
        public void Move(double tx, double ty) { Set(tx, ty, 0, 1, 1, 0, 0); }
        public void Rot(double deg, double px, double py) { Set(0, 0, deg, 1, 1, px, py); }
        public void Scale(double sx, double sy, double px, double py) { Set(0, 0, 0, sx, sy, px, py); }
    }

    public static class G
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static double Clamp(double v, double a, double b) { return Math.Max(a, Math.Min(b, v)); }
        public static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
        public static double Smooth(double u) { u = Clamp(u, 0, 1); return u * u * (3 - 2 * u); }
        public static double Deg(double r) { return r * 180.0 / Math.PI; }
        public static double Rad(double d) { return d * Math.PI / 180.0; }
        public static double Noise(double t, double f) { return (Math.Sin(t * f) + 0.5 * Math.Sin(t * f * 1.73 + 1.3)) / 1.5; }
        static Random rng = new Random(12345);
        public static Random Rng { get { return rng; } }
        public static double Rnd(double a, double b) { return a + rng.NextDouble() * (b - a); }

        public static Color Col(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        public static SolidColorBrush B(string hex)
        {
            SolidColorBrush b = new SolidColorBrush(Col(hex));
            b.Freeze();
            return b;
        }
        public static SolidColorBrush BA(string hex, double alpha)
        {
            Color c = Col(hex); c.A = (byte)(255 * alpha);
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        // Degradado lineal en coordenadas absolutas: stops como "0:#aabbcc" o "0:#aabbcc@0.5" (con alfa)
        public static Brush Lin(double x0, double y0, double x1, double y1, params string[] stops)
        {
            LinearGradientBrush b = new LinearGradientBrush();
            b.MappingMode = BrushMappingMode.Absolute;
            b.StartPoint = new Point(x0, y0); b.EndPoint = new Point(x1, y1);
            AddStops(b, stops);
            b.Freeze();
            return b;
        }
        // Degradado radial en coordenadas absolutas
        public static Brush Rad(double cx, double cy, double rx, double ry, params string[] stops)
        {
            RadialGradientBrush b = new RadialGradientBrush();
            b.MappingMode = BrushMappingMode.Absolute;
            b.Center = new Point(cx, cy); b.GradientOrigin = new Point(cx, cy);
            b.RadiusX = rx; b.RadiusY = ry;
            AddStops(b, stops);
            b.Freeze();
            return b;
        }
        // Degradado radial relativo al objeto (0..1)
        public static Brush RadRel(double cx, double cy, double r, params string[] stops)
        {
            RadialGradientBrush b = new RadialGradientBrush();
            b.Center = new Point(cx, cy); b.GradientOrigin = new Point(cx, cy);
            b.RadiusX = r; b.RadiusY = r;
            AddStops(b, stops);
            b.Freeze();
            return b;
        }
        public static Brush LinRel(double x0, double y0, double x1, double y1, params string[] stops)
        {
            LinearGradientBrush b = new LinearGradientBrush();
            b.StartPoint = new Point(x0, y0); b.EndPoint = new Point(x1, y1);
            AddStops(b, stops);
            b.Freeze();
            return b;
        }
        static void AddStops(GradientBrush b, string[] stops)
        {
            foreach (string s in stops)
            {
                int i = s.IndexOf(':');
                double off = double.Parse(s.Substring(0, i), Inv);
                string col = s.Substring(i + 1);
                double alpha = 1;
                int at = col.IndexOf('@');
                if (at >= 0) { alpha = double.Parse(col.Substring(at + 1), Inv); col = col.Substring(0, at); }
                Color c = Col(col); c.A = (byte)(255 * alpha);
                b.GradientStops.Add(new GradientStop(c, off));
            }
        }

        public static Geometry Geo(string data)
        {
            Geometry g = Geometry.Parse(data);
            g.Freeze();
            return g;
        }
        public static Path P(string data, Brush fill, Brush stroke, double sw)
        {
            return PG(Geo(data), fill, stroke, sw);
        }
        public static Path PG(Geometry geo, Brush fill, Brush stroke, double sw)
        {
            Path p = new Path();
            p.Data = geo;
            p.Fill = fill; p.Stroke = stroke; p.StrokeThickness = sw;
            p.StrokeLineJoin = PenLineJoin.Round; p.StrokeStartLineCap = PenLineCap.Round; p.StrokeEndLineCap = PenLineCap.Round;
            return p;
        }
        public static Ellipse E(double cx, double cy, double rx, double ry, Brush fill, Brush stroke, double sw)
        {
            Ellipse e = new Ellipse();
            e.Width = rx * 2; e.Height = ry * 2;
            Canvas.SetLeft(e, cx - rx); Canvas.SetTop(e, cy - ry);
            e.Fill = fill; e.Stroke = stroke; e.StrokeThickness = sw;
            return e;
        }
        // Elipse con degradado radial suave (relativo a la propia elipse: centro 0.5, radio 0.5)
        public static Ellipse ES(double cx, double cy, double rx, double ry, params string[] stops)
        {
            return E(cx, cy, rx, ry, RadRel(0.5, 0.5, 0.5, stops), null, 0);
        }
        public static Rectangle R(double x, double y, double w, double h, double rad, Brush fill, Brush stroke, double sw)
        {
            Rectangle r = new Rectangle();
            r.Width = w; r.Height = h; r.RadiusX = rad; r.RadiusY = rad;
            Canvas.SetLeft(r, x); Canvas.SetTop(r, y);
            r.Fill = fill; r.Stroke = stroke; r.StrokeThickness = sw;
            return r;
        }
        public static string F(double v) { return v.ToString("0.##", Inv); }

        // Contorno de una elipse como polígono fino
        public static Geometry EllipseGeo(double cx, double cy, double rx, double ry)
        {
            EllipseGeometry g = new EllipseGeometry(new Point(cx, cy), rx, ry);
            g.Freeze();
            return g;
        }

        // Forma afilada siguiendo una curva de Bézier (cola, patas...). Devuelve geometría congelada.
        public static Geometry Taper(Point p0, Point c1, Point c2, Point p1, double w0, double w1, int n)
        {
            List<Point> L = new List<Point>(), R = new List<Point>();
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n, u = 1 - t;
                double x = u * u * u * p0.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * p1.X;
                double y = u * u * u * p0.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * p1.Y;
                double dx = 3 * u * u * (c1.X - p0.X) + 6 * u * t * (c2.X - c1.X) + 3 * t * t * (p1.X - c2.X);
                double dy = 3 * u * u * (c1.Y - p0.Y) + 6 * u * t * (c2.Y - c1.Y) + 3 * t * t * (p1.Y - c2.Y);
                double len = Math.Sqrt(dx * dx + dy * dy); if (len < 1e-6) len = 1;
                double nx = -dy / len, ny = dx / len;
                double sm = t * t * (3 - 2 * t) * 0.6 + t * 0.4;
                double w = (w0 + (w1 - w0) * sm) / 2;
                L.Add(new Point(x + nx * w, y + ny * w)); R.Add(new Point(x - nx * w, y - ny * w));
            }
            StreamGeometry g = new StreamGeometry(); g.FillRule = FillRule.Nonzero;
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(L[0], true, true);
                for (int i = 1; i < L.Count; i++) c.LineTo(L[i], true, true);
                double rr = w1 / 2;
                c.ArcTo(R[R.Count - 1], new Size(rr, rr), 0, false, SweepDirection.Counterclockwise, true, true);
                for (int i = R.Count - 2; i >= 0; i--) c.LineTo(R[i], true, true);
            }
            g.Freeze();
            return g;
        }

        // Segmento recto afilado (patas): grosor w0 en el inicio y w1 en el final, con extremos redondeados
        public static Geometry Seg(double x0, double y0, double x1, double y1, double w0, double w1)
        {
            double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy); if (len < 1e-6) len = 1;
            double nx = -dy / len, ny = dx / len;
            StreamGeometry g = new StreamGeometry(); g.FillRule = FillRule.Nonzero;
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(x0 + nx * w0 / 2, y0 + ny * w0 / 2), true, true);
                c.LineTo(new Point(x1 + nx * w1 / 2, y1 + ny * w1 / 2), true, true);
                c.ArcTo(new Point(x1 - nx * w1 / 2, y1 - ny * w1 / 2), new Size(w1 / 2, w1 / 2), 0, false, SweepDirection.Counterclockwise, true, true);
                c.LineTo(new Point(x0 - nx * w0 / 2, y0 - ny * w0 / 2), true, true);
                c.ArcTo(new Point(x0 + nx * w0 / 2, y0 + ny * w0 / 2), new Size(w0 / 2, w0 / 2), 0, false, SweepDirection.Counterclockwise, true, true);
            }
            g.Freeze();
            return g;
        }
        public static Geometry Line(double x0, double y0, double x1, double y1)
        {
            StreamGeometry g = new StreamGeometry(); g.FillRule = FillRule.Nonzero;
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(x0, y0), false, false);
                c.LineTo(new Point(x1, y1), true, false);
            }
            g.Freeze();
            return g;
        }
    }
}
