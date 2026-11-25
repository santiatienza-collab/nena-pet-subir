// Nena con animación de sprites (6 hojas de 16 fotogramas, todos alineados con la misma cámara: no hay saltos).
// Funciones: teclear (golpes que suben de intensidad con el ritmo, enfado/explosión al teclear muy rápido, cansancio después),
// ojos que siguen al cursor, ojos entornados + run run cada 15 s, miau al clic, aburrimiento (bostezo, mirar arriba, dormir...).
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace NenaPet
{
    public class CatView : Canvas
    {
        public AudioPlayer Audio;
        public Action<int, double> WalkCommand;
        public double ScaleToDip = 0.8;
        public int WalkDir = 1;

        const double W = 300, H = 169;          // lienzo (proporción de los fotogramas 327x184)
        Dictionary<string, BitmapSource> fr = new Dictionary<string, BitmapSource>();
        Image img = new Image(), img2 = new Image();
        Random rnd = new Random();

        double lastKey = -1e9, lastAct = 0;
        int keyIdx = 0;
        List<double> keyTimes = new List<double>();
        bool havePrev; double curX = 150, curY = 90, prevX = -1, prevY = -1,  lastMove = -1e9;
        string mode = "idle"; double modeStart, modeEnd;
        double nextPurr = 15000, nextBored = 20000, fastSince = -1; bool tired;
        double nowT, fadeStart = -1e9, fadeLen = 100, frameChange = -1e9; string curName;
        string boredKind = "yawn", lastKind = "";
        string gaze = "C"; double gazeChange = -1e9;
        double blinkAt = 9000;
        Canvas eyes = new Canvas(); Ellipse[] pupil = new Ellipse[2], glint = new Ellipse[2]; Polygon[] lid = new Polygon[2], lidLo = new Polygon[2]; System.Windows.Shapes.Path[] lidLine = new System.Windows.Shapes.Path[2];
        double egx, egy, lidC = 0.38, lidA = 3.5, blinkUntil = -1, saccX, saccY, saccNext, kickAt = -1e9, lastTick;
        static readonly double[] EX = { 136.5, 184 }; const double EY = 72, ER = 10.2;
        const double PS = 1.0; Canvas pet = new Canvas(); Canvas stage = new Canvas(); RotateTransform stageRot = new RotateTransform(0); TranslateTransform stageT = new TranslateTransform(); ScaleTransform stageSc = new ScaleTransform(1, 1);
        double stageX, puppetX, puppetKick = -1e9, puppetDir = 1;
        Image[] earImg = new Image[2]; RotateTransform[] earRot = new RotateTransform[2]; double earNext = 4000, earStart = -1e9; int earSide; bool earDouble;
        void BuildStage()
        {
            stage.Width = W; stage.Height = H; stage.RenderTransformOrigin = new Point(0.5, 1.0);
            TransformGroup tg = new TransformGroup(); tg.Children.Add(stageSc); tg.Children.Add(stageRot); tg.Children.Add(stageT); stage.RenderTransform = tg;
            pet.Width = 300; pet.Height = H; pet.RenderTransformOrigin = new Point(0, 1); pet.RenderTransform = new ScaleTransform(PS, PS);
            Children.Add(pet); pet.Children.Add(stage);
        }
        void BuildEars()
        {
            double[] px = { 126, 194 };
            for (int i = 0; i < 2; i++)
            {
                earImg[i] = new Image(); earImg[i].Width = W; earImg[i].Height = H; earImg[i].IsHitTestVisible = false; earImg[i].Opacity = 0;
                earImg[i].RenderTransformOrigin = new Point(px[i] / 327.0, 48.0 / 184.0);
                earRot[i] = new RotateTransform(0); earImg[i].RenderTransform = earRot[i];
                Canvas.SetLeft(earImg[i], 0); Canvas.SetTop(earImg[i], 0); stage.Children.Add(earImg[i]);
            }
        }
        // De vez en cuando mueve un poquito las orejas (una, la otra o las dos; a veces dos sacudidas seguidas)
        void UpdateEars(double now)
        {
            bool vis = curName == "B01n" || curName == "P_base";
            if (now >= earNext) { earStart = now; earSide = rnd.Next(3); earDouble = rnd.NextDouble() < 0.35; earNext = now + 3500 + rnd.NextDouble() * 6500; }
            double f = 0, u = (now - earStart) / 200.0, u2 = (now - earStart - 270) / 200.0;
            if (u >= 0 && u <= 1) f = Math.Sin(Math.PI * u); else if (earDouble && u2 >= 0 && u2 <= 1) f = Math.Sin(Math.PI * u2) * 0.8;
            for (int i = 0; i < 2; i++)
            {
                earImg[i].Opacity = vis ? eyes.Opacity : 0;
                bool on = earSide == 2 || earSide == i;
                earRot[i].Angle = (on ? (i == 0 ? -1 : 1) * 4.2 * f : 0) + (i == 0 ? -1 : 1) * earPerk;
            }
        }
        // Al tocar el cuerpo entero (cuando "lo maneja un humano") vuelve suavemente a su sitio al dejar de teclear
        void StageStep(double now, double dt, double cl)
        {
            double k = W / 327.0, pulse = Math.Exp(-(now - puppetKick) / 120.0);
            stageX += (puppetX - stageX) * Math.Min(1, dt * 10);
            stageT.X = stageX * k; stageT.Y = cl * 1.2 * pulse * k;
            stageRot.Angle = cl * (puppetDir * 0.9 * pulse + stageX * 0.08); stageSc.ScaleY = 1 - cl * 0.012 * pulse;
        }
        Canvas moodHost = new Canvas(); Canvas mouthG = new Canvas(), arcsG = new Canvas(), blushG = new Canvas();
        double happyK, funnyK, eyeCross, pupilS = 1, earPerk; int relaxBase; double relaxOff; bool wasRelaxed; bool purrOn;
        void BuildMood()
        {
            moodHost.Width = 327; moodHost.Height = 184; moodHost.IsHitTestVisible = false; moodHost.Opacity = 0;
            moodHost.RenderTransform = new ScaleTransform(W / 327.0, W / 327.0);
            Brush pink = new SolidColorBrush(Color.FromArgb(150, 255, 140, 160)), cream = new SolidColorBrush(Color.FromRgb(252, 249, 244)), dark = new SolidColorBrush(Color.FromRgb(30, 18, 14)), arc = new SolidColorBrush(Color.FromRgb(238, 224, 210));
            double[] bx = { 124, 197 };
            for (int i = 0; i < 2; i++)
            {
                Ellipse b = new Ellipse(); b.Width = 17; b.Height = 9; b.Fill = pink; Canvas.SetLeft(b, bx[i] - 8.5); Canvas.SetTop(b, 87); blushG.Children.Add(b);
                System.Windows.Shapes.Path p = new System.Windows.Shapes.Path();
                p.Data = Geometry.Parse("M" + G.F(EX[i] - 8) + "," + G.F(EY + 3.5) + " Q" + G.F(EX[i]) + "," + G.F(EY - 8) + " " + G.F(EX[i] + 8) + "," + G.F(EY + 3.5));
                p.Stroke = arc; p.StrokeThickness = 2.4; p.StrokeStartLineCap = PenLineCap.Round; p.StrokeEndLineCap = PenLineCap.Round; arcsG.Children.Add(p);
            }
            Rectangle cov = new Rectangle(); cov.Width = 26; cov.Height = 15; cov.RadiusX = 5; cov.RadiusY = 5; cov.Fill = cream; Canvas.SetLeft(cov, 148); Canvas.SetTop(cov, 89); mouthG.Children.Add(cov);
            System.Windows.Shapes.Path sm = new System.Windows.Shapes.Path(); sm.Data = Geometry.Parse("M153,98.4 Q161,96.4 169,98.4 M161,89 L161,95");   // boca seria con colmillo (relajada)
            sm.Stroke = dark; sm.StrokeThickness = 1.6; sm.StrokeStartLineCap = PenLineCap.Round; sm.StrokeEndLineCap = PenLineCap.Round; sm.StrokeLineJoin = PenLineJoin.Round; mouthG.Children.Add(sm);
            System.Windows.Shapes.Path fang = new System.Windows.Shapes.Path(); fang.Data = Geometry.Parse("M154.2,97.4 L158.6,98 L156.2,104.6 Z"); fang.Fill = Brushes.White; fang.Stroke = dark; fang.StrokeThickness = 0.9; fang.StrokeLineJoin = PenLineJoin.Round; mouthG.Children.Add(fang);
            blushG.Opacity = 0; arcsG.Opacity = 0; mouthG.Opacity = 0;
            moodHost.Children.Add(blushG); moodHost.Children.Add(mouthG); moodHost.Children.Add(arcsG);
        }
        void MoodStep(double now, double dt, double hT, double fT)
        {
            double k = Math.Min(1, dt * 4);
            happyK += (hT - happyK) * k; funnyK += (fT - funnyK) * k;
            moodHost.Opacity = eyes.Opacity;
            mouthG.Opacity = funnyK; arcsG.Opacity = 0; blushG.Opacity = 0;
            pupilS += ((1 + 0.7 * happyK) - pupilS) * k; earPerk = 5.0 * happyK - 4.0 * funnyK;
            Canvas.SetTop(moodHost, Canvas.GetTop(img));
            // balanceo muy ligero (y un leve temblor al ronronear)
            double kk = W / 327.0, t = now / 1000.0;
            stageRot.Angle = 0;
            stageT.Y = (-2.5 * happyK + 1.5 * funnyK + 0.5 * Math.Sin(t * 55) * funnyK) * kk; stageT.X = 0.35 * Math.Sin(t * 47 + 1) * funnyK * kk;   // temblor del ronroneo (funnyK = modo relajado) stageSc.ScaleX = stageSc.ScaleY = 1 + 0.05 * happyK; stageRot.Angle += egx * 2.2 * happyK;
        }
        Canvas mouseGrp = new Canvas(); TranslateTransform mouseT = new TranslateTransform(); System.Windows.Shapes.Path armPath = new System.Windows.Shapes.Path(); Image mousePaw = new Image(); TranslateTransform mousePawT = new TranslateTransform(); RotateTransform mousePawR = new RotateTransform(0); ScaleTransform mousePawS = new ScaleTransform(1, 1);
        double mouseTX, mouseTY, mox, moy, armK; const double MX0 = 221, MY0 = 152;
void BuildKeyboard()
        {
            Brush chassis = new SolidColorBrush(Color.FromRgb(58, 60, 70)), edge = new SolidColorBrush(Color.FromRgb(20, 20, 26)), front = new SolidColorBrush(Color.FromRgb(38, 39, 46)), key = new SolidColorBrush(Color.FromRgb(30, 31, 38)), keyEdge = new SolidColorBrush(Color.FromRgb(92, 95, 108));
            Polygon top = new Polygon(); top.Points = new PointCollection(new Point[] { new Point(84, 139), new Point(190, 139), new Point(201, 163), new Point(73, 163) });
            top.Fill = chassis; top.Stroke = edge; top.StrokeThickness = 2; top.StrokeLineJoin = PenLineJoin.Round; pet.Children.Add(top);
            Polygon fr = new Polygon(); fr.Points = new PointCollection(new Point[] { new Point(73, 163), new Point(201, 163), new Point(203, 167), new Point(71, 167) }); fr.Fill = front; fr.Stroke = edge; fr.StrokeThickness = 1.2; pet.Children.Add(fr);
            int[] counts = { 9, 8, 8, 6 };
            for (int r = 0; r < 4; r++)
            {
                double y = 141.5 + r * 5.5, t = (y - 139) / 24.0;
                double xl = 84 - 11 * t + 4, xr = 190 + 11 * t - 4, w = (xr - xl) / counts[r];
                for (int c = 0; c < counts[r]; c++)
                {
                    if (r == 3 && (c == 3 || c == 4)) continue;
                    bool space = r == 3 && c == 2;
                    Rectangle k = new Rectangle(); k.Width = (space ? w * 3 : w) - 1.5; k.Height = 4.0; k.RadiusX = 1; k.RadiusY = 1; k.Fill = key; k.Stroke = keyEdge; k.StrokeThickness = 0.5;
                    Canvas.SetLeft(k, xl + c * w + 0.75); Canvas.SetTop(k, y); pet.Children.Add(k);
                }
            }
        }
        void BuildMouse()
        {
            Brush top = new SolidColorBrush(Color.FromRgb(78, 82, 96)), side = new SolidColorBrush(Color.FromRgb(40, 43, 52)), edge = new SolidColorBrush(Color.FromRgb(22, 22, 28)), light = new SolidColorBrush(Color.FromRgb(214, 218, 226));
            Ellipse sh = new Ellipse(); sh.Width = 30; sh.Height = 9; sh.Fill = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)); Canvas.SetLeft(sh, -15); Canvas.SetTop(sh, 13); mouseGrp.Children.Add(sh);
            System.Windows.Shapes.Path cable = new System.Windows.Shapes.Path(); cable.Data = Geometry.Parse("M0,15 C2,19 8,20 15,21"); cable.Stroke = edge; cable.StrokeThickness = 1.8; cable.StrokeStartLineCap = PenLineCap.Round; cable.StrokeEndLineCap = PenLineCap.Round; mouseGrp.Children.Add(cable);
            // canto (grosor del ratón, visto en perspectiva)
            System.Windows.Shapes.Path sd = new System.Windows.Shapes.Path(); sd.Data = Geometry.Parse("M-9,-13 Q0,-16 9,-13 Q13,-10 13,2 Q13,18 0,20 Q-13,18 -13,2 Q-13,-10 -9,-13 Z"); sd.Fill = side; sd.Stroke = edge; sd.StrokeThickness = 2; sd.StrokeLineJoin = PenLineJoin.Round; mouseGrp.Children.Add(sd);
            // cara superior: más estrecha atrás (lejos) y más ancha delante (cerca)
            System.Windows.Shapes.Path tp = new System.Windows.Shapes.Path(); tp.Data = Geometry.Parse("M-8,-17 Q0,-20 8,-17 Q12,-14 13,-2 Q13,14 0,16 Q-13,14 -13,-2 Q-12,-14 -8,-17 Z"); tp.Fill = top; tp.Stroke = edge; tp.StrokeThickness = 2; tp.StrokeLineJoin = PenLineJoin.Round; mouseGrp.Children.Add(tp);
            // botones y rueda en la parte delantera (hacia el lado contrario a la gata)
            System.Windows.Shapes.Path btn = new System.Windows.Shapes.Path(); btn.Data = Geometry.Parse("M-12,2 Q0,5 12,2 M0,15 L0,3.5"); btn.Stroke = edge; btn.StrokeThickness = 1.4; btn.StrokeLineJoin = PenLineJoin.Round; mouseGrp.Children.Add(btn);
            Rectangle wh = new Rectangle(); wh.Width = 3.6; wh.Height = 7; wh.RadiusX = 1.8; wh.RadiusY = 1.8; wh.Fill = light; wh.Stroke = edge; wh.StrokeThickness = 0.9; Canvas.SetLeft(wh, -1.8); Canvas.SetTop(wh, 3.5); mouseGrp.Children.Add(wh);
            Ellipse hl = new Ellipse(); hl.Width = 11; hl.Height = 6; hl.Fill = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)); Canvas.SetLeft(hl, -5.5); Canvas.SetTop(hl, -13); mouseGrp.Children.Add(hl);
            mouseT.X = MX0; mouseT.Y = MY0; { TransformGroup mg = new TransformGroup(); mg.Children.Add(new ScaleTransform(0.85, 0.85)); mg.Children.Add(mouseT); mouseGrp.RenderTransform = mg; } pet.Children.Add(mouseGrp);
            armPath.Stroke = new SolidColorBrush(Color.FromRgb(44, 29, 23)); armPath.StrokeThickness = 15; armPath.StrokeStartLineCap = PenLineCap.Round; armPath.StrokeEndLineCap = PenLineCap.Round; armPath.Opacity = 0;
            pet.Children.Add(armPath);
            mousePaw.Width = W; mousePaw.Height = H; mousePaw.IsHitTestVisible = false; mousePaw.Opacity = 0; mousePaw.RenderTransformOrigin = new Point(201.0 / 327.0, 154.0 / 184.0); { TransformGroup pg = new TransformGroup(); pg.Children.Add(mousePawS); pg.Children.Add(mousePawR); pg.Children.Add(mousePawT); mousePaw.RenderTransform = pg; } Canvas.SetLeft(mousePaw, 0); Canvas.SetTop(mousePaw, 0); pet.Children.Add(mousePaw);
        }
        // Cuando mueves el cursor, la gata estira la pata derecha hasta el ratón y lo arrastra; al teclear lo suelta
        void UpdateMouse(double now, double dt, bool typing)
        {
            bool want = !typing && now - lastMove < 1400;
            armK += ((want ? 1 : 0) - armK) * Math.Min(1, dt * 7);
            mox += (mouseTX - mox) * Math.Min(1, dt * 14); moy += (mouseTY - moy) * Math.Min(1, dt * 14);
            double dec = Math.Pow(0.25, dt); mouseTX *= dec; mouseTY *= dec;
            double mx = MX0 + mox, my = MY0 + moy;
            mouseT.X = mx; mouseT.Y = my;
            double px = mx - 1, py = my - 4;
            // el antebrazo sale del muñón del brazo derecho de la gata y llega, corto, hasta el ratón
            double sx = 184, sy = 143;
            armPath.Data = Geometry.Parse("M" + G.F(sx) + "," + G.F(sy) + " Q" + G.F((sx + px) / 2) + "," + G.F(Math.Max(sy, py) + 5) + " " + G.F(px - 3) + "," + G.F(py - 1));
            armPath.Opacity = armK;
            if (fr.ContainsKey("P_R")) { if (mousePaw.Source == null) mousePaw.Source = fr["P_R"]; }
            mousePawR.Angle = 9 * armK; mousePawS.ScaleX = 1 + 0.08 * armK; mousePawS.ScaleY = 1 + 0.12 * armK;
            mousePawT.X = px - 184.0; mousePawT.Y = py - 140.0; mousePaw.Opacity = armK;
        }
        Image[] pawImg = new Image[2]; TranslateTransform[] pawT = new TranslateTransform[2]; RotateTransform[] pawRot = new RotateTransform[2]; ScaleTransform[] pawSc = new ScaleTransform[2];
        double[] hitAt = { -1e9, -1e9 }, hitRest = { -11, -11 }, hitTau = { 100, 100 }, hitJx = { 0, 0 }, hitRotA = { 0, 0 }, hitScale = { 1, 1 }, hitDepth = { 2, 2 }, hitCl = { 0, 0 }, hitStr = { 0, 0 }; double clumsy;
        double prevKeyT = -1e9; int burst = 0;
        static readonly double[] PCX = { 126, 201 }, PCY = { 153, 154 };
        Canvas sparkHost = new Canvas(); System.Windows.Shapes.Path[] spark = new System.Windows.Shapes.Path[2];
        void BuildPaws()
        {
            for (int i = 0; i < 2; i++)
            {
                pawImg[i] = new Image(); pawImg[i].Width = W; pawImg[i].Height = H; pawImg[i].IsHitTestVisible = false; pawImg[i].Opacity = 0;
                pawT[i] = new TranslateTransform(); pawRot[i] = new RotateTransform(0); pawSc[i] = new ScaleTransform(1, 1);
                pawImg[i].RenderTransformOrigin = new Point(PCX[i] / 327.0, 140.0 / 184.0);   // la pata se estira hacia abajo desde la muñeca
                TransformGroup tg = new TransformGroup(); tg.Children.Add(pawSc[i]); tg.Children.Add(pawRot[i]); tg.Children.Add(pawT[i]); pawImg[i].RenderTransform = tg;
                Canvas.SetLeft(pawImg[i], 0); Canvas.SetTop(pawImg[i], 0); stage.Children.Add(pawImg[i]);
            }
            sparkHost.Width = 327; sparkHost.Height = 184; sparkHost.IsHitTestVisible = false; sparkHost.RenderTransform = new ScaleTransform(W / 327.0, W / 327.0);
            for (int i = 0; i < 2; i++)
            {
                spark[i] = new System.Windows.Shapes.Path(); spark[i].Data = Geometry.Parse("M-21,-3 h12 v7 h-12 z M-6,-3 h12 v7 h-12 z M9,-3 h12 v7 h-12 z"); spark[i].Fill = new SolidColorBrush(Color.FromRgb(228, 228, 238));
                spark[i].StrokeThickness = 0; spark[i].StrokeStartLineCap = PenLineCap.Round; spark[i].StrokeEndLineCap = PenLineCap.Round; spark[i].Opacity = 0;
                sparkHost.Children.Add(spark[i]);
            }
            stage.Children.Add(sparkHost);
        }
        // Cada tecla baja una pata distinta (izquierda/derecha); las dos se mantienen algo levantadas mientras escribe. El cuerpo no se mueve.
        void UpdatePaws(double now, bool on)
        {
            double fade = on ? Math.Min(1, (now - frameChange) / 90.0) : 0;
            double[] sx = { 126, 201 };
            for (int i = 0; i < 2; i++)
            {
                double t = Math.Max(0, now - hitAt[i]);
                double y = now < hitAt[i] ? hitRest[i] : hitRest[i] + (hitDepth[i] - hitRest[i]) * Math.Exp(-t / hitTau[i]);
                y += Math.Sin(now / 130.0 + i * 2.1) * 1.6 * hitCl[i] * Math.Min(1, t / 250.0);   // titubeo: la pata tiembla un poco en el aire
                double k = W / 327.0, ex = Math.Exp(-t / (hitTau[i] * 0.9));      // la pata se estira y barre el teclado al golpear, y se recoge despacio
                pawImg[i].Opacity = fade; pawT[i].Y = y * k; pawT[i].X = hitJx[i] * ex * k;
                pawRot[i].Angle = hitRotA[i] * ex; pawSc[i].ScaleX = 1 + (hitScale[i] - 1) * ex; pawSc[i].ScaleY = 1 + hitStr[i] * ex;
                double so = (on && now >= hitAt[i] && t < 170) ? (1 - t / 170.0) * 0.6 : 0;
                spark[i].Opacity = so; spark[i].RenderTransform = new TranslateTransform(sx[i] + hitJx[i] * 0.8, 170);
            }
        }
        void HidePaws() { for (int i = 0; i < 2; i++) { pawImg[i].Opacity = 0; spark[i].Opacity = 0; } }

        void EyesStep(double now, double dt, double tgx, double tgy, double cT, double aT, double lo, double sm)
        {
            if (now > saccNext) { saccX = (rnd.NextDouble() - 0.5) * 0.3; saccY = (rnd.NextDouble() - 0.5) * 0.25; saccNext = now + 700 + rnd.NextDouble() * 1600; }
            double k1 = Math.Min(1, dt * (16 + 26 * happyK));
            egx += (Clamp(tgx + saccX * sm, -1, 1) - egx) * k1; egy += (Clamp(tgy + saccY * sm, -1, 1) - egy) * k1;
            if (now >= blinkAt) { blinkUntil = now + 110; blinkAt = now + 8000 + rnd.NextDouble() * 9000; if (rnd.NextDouble() < 0.07) blinkAt = now + 280; }
            if (now < blinkUntil) cT = 1.08;
            double kl = Math.Min(1, dt * (now < blinkUntil ? 40 : 12));
            lidC += (cT - lidC) * kl; lidA += (aT - lidA) * Math.Min(1, dt * 8);
            PlaceEyes(egx, egy, lidC, lidA, lo);
            eyes.Opacity = Math.Min(1, (now - frameChange) / 110.0);
        }
        Canvas veinHost = new Canvas(), vein = new Canvas();
        TranslateTransform veinT = new TranslateTransform(); ScaleTransform veinS = new ScaleTransform(1, 1); RotateTransform veinR = new RotateTransform(0);
        void BuildVein()
        {
            veinHost.Width = 327; veinHost.Height = 184; veinHost.IsHitTestVisible = false; veinHost.Opacity = 0;
            veinHost.RenderTransform = new ScaleTransform(W / 327.0, W / 327.0);
            Brush red = new SolidColorBrush(Color.FromRgb(238, 28, 37)), edge = new SolidColorBrush(Color.FromRgb(107, 0, 8));
            for (int i = 0; i < 4; i++)
            {
                System.Windows.Shapes.Path sp = new System.Windows.Shapes.Path();
                sp.Data = Geometry.Parse("M3,-3 L2.5,-19 Q6,-7 19,-2.5 Z");
                sp.Fill = red; sp.Stroke = edge; sp.StrokeThickness = 1.8; sp.StrokeLineJoin = PenLineJoin.Round;
                sp.RenderTransform = new RotateTransform(90 * i);
                vein.Children.Add(sp);
            }
            TransformGroup tg = new TransformGroup(); tg.Children.Add(veinS); tg.Children.Add(veinR); tg.Children.Add(veinT);
            vein.RenderTransform = tg; veinHost.Children.Add(vein);
        }
        // sien (derecha de la cabeza); en las poses en espejo, la izquierda
        void ShowVein(double now, double k, string frame, double oy)
        {
            if (k <= 0.01) { veinHost.Opacity = 0; return; }
            bool mirror = frame.EndsWith("m");
            double x = mirror ? 327 - 204 : 204, y = 40;
            double pulse = 1 + 0.22 * Math.Max(0, Math.Sin(now / 1000.0 * 14)), pop = 0.55 + 0.45 * k;
            veinS.ScaleX = veinS.ScaleY = 0.62 * pulse * pop; veinR.Angle = Math.Sin(now / 1000.0 * 9) * 4;
            veinT.X = x; veinT.Y = y + oy / (W / 327.0);
            veinHost.Opacity = Math.Min(1, k * 1.5);
        }
        string[] purrSeq = { "D03", "D07", "D11", "D07", "D03", "D10", "D06", "D10" };

        public CatView()
        {
            Width = W; Height = H; Background = null; SnapsToDevicePixels = false;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            BuildStage(); stage.Children.Add(img2); stage.Children.Add(img); BuildEars(); BuildPaws(); BuildEyes(); stage.Children.Add(eyes); BuildMood(); stage.Children.Add(moodHost); BuildVein(); stage.Children.Add(veinHost);
            img.IsHitTestVisible = false; img2.IsHitTestVisible = false;
            Assembly a = Assembly.GetExecutingAssembly();
            foreach (string ex in new string[] { "B01n", "A05m", "F05m", "P_base", "P_L", "P_R", "E_L", "E_R" }) LoadFrame(a, ex);
            foreach (char L in "ABCDEF")
                for (int i = 0; i < 16; i++)
                {
                    LoadFrame(a, L + i.ToString("00"));
                }
            img.Width = W; img.Height = H; img2.Width = W; img2.Height = H;
            Canvas.SetLeft(img, 0); Canvas.SetTop(img, 0); Canvas.SetLeft(img2, 0); Canvas.SetTop(img2, 0);
            if (fr.ContainsKey("P_L")) { pawImg[0].Source = fr["P_L"]; pawImg[1].Source = fr["P_R"]; }
            if (fr.ContainsKey("E_L")) { earImg[0].Source = fr["E_L"]; earImg[1].Source = fr["E_R"]; }
            Show("B01n", 0, 1);
        }

        void LoadFrame(Assembly a, string n)
        {
            try
            {
                using (Stream s = a.GetManifestResourceStream("f." + n + ".png"))
                {
                    if (s == null) return;
                    BitmapImage b = new BitmapImage(); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.StreamSource = s; b.EndInit(); b.Freeze();
                    fr[n] = b;
                }
            }
            catch (Exception) { }
        }

        // Ojos dibujados en vivo sobre el fotograma sin pupilas: mirada, párpados y parpadeo continuos.
        void BuildEyes()
        {
            eyes.Width = 327; eyes.Height = 184; eyes.IsHitTestVisible = false; eyes.Opacity = 0;
            eyes.RenderTransformOrigin = new Point(0, 0);
            eyes.RenderTransform = new ScaleTransform(W / 327.0, W / 327.0);
            Brush dark = new SolidColorBrush(Color.FromRgb(20, 12, 14)), lidBrush = new SolidColorBrush(Color.FromRgb(42, 28, 22)), line = new SolidColorBrush(Color.FromRgb(30, 18, 14));
            for (int i = 0; i < 2; i++)
            {
                Canvas g = new Canvas(); g.Width = 327; g.Height = 184;
                g.Clip = new EllipseGeometry(new Point(EX[i], EY), ER - 0.3, ER - 0.3);
                pupil[i] = new Ellipse(); pupil[i].Width = 6.6; pupil[i].Height = 13; pupil[i].Fill = dark; g.Children.Add(pupil[i]);
                glint[i] = new Ellipse(); glint[i].Width = 3.4; glint[i].Height = 3.8; glint[i].Fill = Brushes.White; glint[i].Opacity = 0.95; g.Children.Add(glint[i]);
                lidLo[i] = new Polygon(); lidLo[i].Fill = lidBrush; g.Children.Add(lidLo[i]);
                lid[i] = new Polygon(); lid[i].Fill = lidBrush; g.Children.Add(lid[i]);
                lidLine[i] = new System.Windows.Shapes.Path(); lidLine[i].Stroke = line; lidLine[i].StrokeThickness = 1.7; lidLine[i].StrokeLineJoin = PenLineJoin.Round; g.Children.Add(lidLine[i]);
                eyes.Children.Add(g);
            }
        }

        // c: cuánto baja el párpado superior (0 abierto .. 1 cerrado); a: inclinación (ceño) en píxeles
        void PlaceEyes(double gx, double gy, double c, double a, double lo)
        {
            for (int i = 0; i < 2; i++)
            {
                double cx = EX[i], cy = EY, dirIn = i == 0 ? 1 : -1;     // hacia el centro de la cara
                double px = cx + gx * 5.0 + dirIn * eyeCross * 4.0, py = cy + gy * 3.0;
                pupil[i].Width = 6.6 * pupilS; pupil[i].Height = 13 * pupilS; Canvas.SetLeft(pupil[i], px - 3.3 * pupilS); Canvas.SetTop(pupil[i], py - 6.5 * pupilS);
                Canvas.SetLeft(glint[i], px - 3.2); Canvas.SetTop(glint[i], py - 5.6);
                double top = cy - ER, h = ER * 2;
                double yOut = top + c * h - a * 0.5, yIn = top + c * h + a;
                double xo = cx - dirIn * (ER + 1), xi = cx + dirIn * (ER + 1);
                lid[i].Points = new PointCollection(new Point[] { new Point(xo, top - 3), new Point(xi, top - 3), new Point(xi, yIn), new Point(xo, yOut) });
                lidLine[i].Data = Geometry.Parse("M" + G.F(xo) + "," + G.F(yOut) + " L" + G.F(xi) + "," + G.F(yIn));
                double yl = cy + ER - lo * h;
                lidLo[i].Points = new PointCollection(new Point[] { new Point(cx - ER - 1, yl), new Point(cx + ER + 1, yl), new Point(cx + ER + 1, cy + ER + 3), new Point(cx - ER - 1, cy + ER + 3) });
            }
        }

        // ------------------------------------------------------------------ API
        public void Activity(double now) { lastAct = now; }
        public void SetCursor(double x, double y, double now)
        {
            double k = ScaleToDip > 0 ? 1.0 / ScaleToDip : 1.25;
            double nx = x * k / PS, ny = (y * k - H * (1 - PS)) / PS;
            if (havePrev) { mouseTX = Clamp(mouseTX + (nx - curX) * 0.4, -5, 9); mouseTY = Clamp(mouseTY + (ny - curY) * 0.4, -4, 4); }
            curX = nx; curY = ny;
            if (havePrev && Math.Abs(x - prevX) + Math.Abs(y - prevY) > 2) { lastMove = now; lastAct = now; }
            prevX = x; prevY = y; havePrev = true;
        }
        public void KeyPress(double now)
        {
            if (mode != "type") StopPurr();
            if (now - prevKeyT > 1500) burst = 0;
            prevKeyT = now; burst++;
            lastKey = now; lastAct = now; keyIdx++; kickAt = now;
            {
                // los primeros golpes de una racha son torpes: patita entera, más arriba, algo torcida y a destiempo; luego se afina
                // los primeros golpes de una racha: como si un humano la sujetara por detrás y le aporreara el teclado con las patas;
                // luego va tomando el control ella sola
                int p = keyIdx % 2; double cl = Math.Max(0, 1 - (burst - 1) / 10.0); clumsy = cl;
                bool both = cl > 0.2 && rnd.NextDouble() < 0.55 * cl;
                for (int q = 0; q < 2; q++)
                {
                    if (!(both || q == p)) continue;
                    hitAt[q] = now; hitRest[q] = -(11 + 14 * cl); hitTau[q] = 100 - 30 * cl; hitCl[q] = 0;
                    hitJx[q] = (rnd.NextDouble() * 2 - 1) * (5 + 5 * rnd.NextDouble()); hitRotA[q] = (rnd.NextDouble() * 2 - 1) * 3; hitScale[q] = 1.12 + rnd.NextDouble() * 0.2; hitStr[q] = 0.28 + rnd.NextDouble() * 0.32; hitDepth[q] = 2 + 2 * cl;
                }
                puppetKick = now; puppetDir = rnd.NextDouble() < 0.5 ? -1 : 1; puppetX = (rnd.NextDouble() * 2 - 1) * 2.5 * cl;
            }
            keyTimes.Add(now); while (keyTimes.Count > 0 && now - keyTimes[0] > 1000) keyTimes.RemoveAt(0);
            mode = "type";
        }
        public void Click(double now)
        {
            StopPurr();
            double d = Audio != null ? Audio.PlayMeow() : 0.8;
            lastAct = now; mode = "meow"; modeStart = now; modeEnd = now + Math.Max(700, d * 1000);
        }
        public void WalkDirFromHost(int d) { WalkDir = d; }
        public bool keepAwake = true;
        public void Simulate(double seconds, double now0) { }
        void StopPurr() { if (Audio != null) Audio.StopPurr(); }

        // ------------------------------------------------------------------ dibujo
        void Show(string name, double oy, double sy, double len = 100)
        {
            BitmapSource b; if (!fr.TryGetValue(name, out b)) return;
            if (curName != name)
            {
                if (img.Source != null)
                {
                    img2.Source = img.Source; img2.RenderTransform = img.RenderTransform; img2.RenderTransformOrigin = img.RenderTransformOrigin;
                    Canvas.SetTop(img2, Canvas.GetTop(img));
                    fadeStart = nowT; fadeLen = len;
                }
                img.Source = b; curName = name; frameChange = nowT;
            }
            Canvas.SetTop(img, oy); Canvas.SetTop(eyes, oy); if (name != "B01n" && name != "P_base") eyes.Opacity = 0;
            img.RenderTransformOrigin = new Point(0.5, 1.0);
            img.RenderTransform = sy == 1 ? Transform.Identity : new ScaleTransform(1, sy);
        }

        static string Pick(string[] a, int i) { return a[((i % a.Length) + a.Length) % a.Length]; }
        void ToIdle(double now) { mode = "idle"; nextPurr = now + 6300; nextBored = now + 20000; }

        string[] T0 = { "A05", "A05m" };
        string[] T1 = { "F05", "F05m" };
        string[] T2 = { "F05", "A06", "F05m", "A08" };
        string[] T3 = { "A10", "A11", "F10", "F11" };

        public void Tick(double now)
        {
            nowT = now; if (now - lastKey >= 650) veinHost.Opacity = 0;
            double dt = Math.Min(0.05, Math.Max(0.001, (now - lastTick) / 1000.0)); lastTick = now;
            if (now - lastKey >= 650) { HidePaws(); puppetX = 0; clumsy = 0; StageStep(now, dt, 0); stageSc.ScaleX = 1; }
            UpdateEars(now);

            img2.Opacity = Math.Max(0, 1 - (now - fadeStart) / fadeLen);
            if (Audio != null) Audio.Update(now);

            while (keyTimes.Count > 0 && now - keyTimes[0] > 1000) keyTimes.RemoveAt(0);
            double rate = keyTimes.Count;
            bool typing = now - lastKey < 650;
            double sinceAct = now - lastAct;

            // ---- teclear: cada tecla avanza un fotograma de la secuencia del nivel de intensidad ----
            if (typing)
            {
                mode = "type";
                if (rate >= 7) { if (fastSince < 0) fastSince = now; if (now - fastSince > 2500) tired = true; }
                else if (rate < 3) fastSince = -1;
                if (rate >= 10)
                {
                    // teclear a toda velocidad: explosiones
                    string n = Pick(T3, keyIdx);
                    if (n != curName && now - frameChange < 70) n = curName;
                    Show(n, 0, 1, 80); HidePaws(); puppetX = 0; StageStep(now, dt, 0);
                    ShowVein(now, 1, curName, 0);
                }
                else
                {
                    // el cuerpo y la cabeza no se mueven: solo las patas (alternando) y los ojos/ceño
                    Show("P_base", 0, 1, 80);
                    double lvl = Clamp((rate - 3) / 6.0, 0, 1);
                    double cz = clumsy;
                    EyesStep(now, dt, Math.Sin(now / 420.0) * 0.5 * cz, 0.9 * (1 - cz) - 0.1 * cz, (0.30 + 0.12 * lvl) * (1 - cz) + 0.03 * cz, (4.0 + 4.5 * lvl) * (1 - cz) - 1.5 * cz, 0, 0.3);
                    StageStep(now, dt, cz);   // al principio mira las patas con ojos muy abiertos
                    UpdatePaws(now, true);
                    ShowVein(now, Clamp((rate - 5.5) / 2.0, 0, 1), "P_base", 0);
                }
                nextPurr = now + 6300; nextBored = now + 20000;
                return;
            }
            if (mode == "type")
            {
                if (tired) { mode = "tired"; modeStart = now; modeEnd = now + 2600; tired = false; fastSince = -1; }
                else ToIdle(now);
            }
            if (mode == "tired")
            {
                if (now >= modeEnd) ToIdle(now);
                else { double t = now - modeStart; Show("A14", 0, 1, 160); return; }
            }

            // ---- miau ----
            if (mode == "meow")
            {
                if (now >= modeEnd) ToIdle(now);
                else
                {
                    double u = (now - modeStart) / Math.Max(1.0, modeEnd - modeStart);
                    string[] seq = { "E01", "E02", "E03", "E04", "E05", "E06", "E05", "E04", "E03", "E01" };
                    Show(seq[Math.Min(seq.Length - 1, (int)(u * seq.Length))], 0, 1, 80);
                    return;
                }
            }

            // ---- run run ----
            if (mode == "purr")
            {
                if (now >= modeEnd || sinceAct < 1200) { StopPurr(); ToIdle(now); }
                else
                {
                    double t = now - modeStart;
                    string n = t < 350 ? "D00" : t < 700 ? "D01" : t < 1050 ? "D02" : Pick(purrSeq, (int)((t - 1050) / 420));
                    Show(n, 0, 1, 130);
                    return;
                }
            }

            // ---- aburrimiento ----
            if (mode == "bored")
            {
                if (now >= modeEnd || sinceAct < 1500) { ToIdle(now); nextBored = now + 9000 + rnd.NextDouble() * 6000; }
                else { Bored(now); return; }
            }

            // ---- reposo: gruñona; los ojos siguen al cursor con continuidad ----
            bool alert = now - lastMove < 3000, relaxed = sinceAct > 6000;
            double tgx = Clamp((curX - 150) / 110.0, -1, 1), tgy = Clamp((curY - 66) / 80.0, -1, 1);
            double hT = 0, fT = 0, cT = alert ? 0.06 : 0.40, aT = alert ? 0.0 : 3.8, lo = 0;
            eyeCross = 0; int phase = -1;
            if (relaxed)
            {
                // sin tocar nada, cada 5 s cambia de modo: 0 relajada (ojos entornados + run run), 1 grumpy, 2 extremadamente atenta
                if (!wasRelaxed) relaxOff = rnd.NextDouble() * 17500;      // el ciclo ya no empieza siempre por "relajada": cada vez arranca por un modo al azar, así salen igual de a menudo
                wasRelaxed = true;
                double cyc = (relaxOff + (now - (lastAct + 6000))) % 17500;   // relajada 7,5 s, grumpy 2,5 s (3 veces menos), atenta 7,5 s
                phase = cyc < 7500 ? 0 : cyc < 10000 ? 1 : 2;
                hT = phase == 2 ? 1 : 0; fT = phase == 0 ? 1 : 0;      // fT: boca neutra solo en el modo relajado
                if (phase == 0 && now >= nextPurr) { if (Audio != null) Audio.PlayPurr(now); purrOn = true; nextPurr = now + 15000; }
                cT = phase == 0 ? 0.55 : phase == 2 ? -0.06 : 0.45; aT = phase == 0 ? 1.0 : phase == 2 ? -1.4 : 4.5; lo = phase == 0 ? 0.0 : 0;   // relajada: ojos casi cerrados, ceja caída
                if (phase == 0) { tgx *= 0.5; tgy = 0.15 + 0.2 * tgy; }   // mirada baja y perezosa
            }
            else { if (purrOn) { StopPurr(); purrOn = false; } wasRelaxed = false; }
            bool onMouse = armK > 0.05;
            string baseName = onMouse ? "P_base" : "B01n";
            if (!onMouse && phase == 1) { double gdx = curX - 150; baseName = gdx < -70 ? "B09" : gdx > 70 ? "B11" : "B13"; }   // grumpy: la cara gruñona de la hoja, mirando al cursor
            Show(baseName, 0, 1, 160);
            if (onMouse)
            {   // la pata derecha está en el ratón: solo se ve la izquierda sobre el teclado
                pawImg[0].Opacity = Math.Min(1, armK * 1.3); pawT[0].Y = 0; pawRot[0].Angle = 0; pawSc[0].ScaleX = pawSc[0].ScaleY = 1; pawT[0].X = 0; pawImg[1].Opacity = 0;
            }
            EyesStep(now, dt, tgx, tgy, cT, aT, lo, (alert ? 0.3 : 1.0) * (1 - 0.85 * hT));
            MoodStep(now, dt, hT, fT);
            // el divertido mueve más las orejas
            if (happyK > 0.5 && earNext - now > 1800) earNext = now + 700 + rnd.NextDouble() * 1100;
        }

        static double Clamp(double v, double a, double b) { return v < a ? a : (v > b ? b : v); }

        void Bored(double now)
        {
            double t = now - modeStart; string n;
            switch (boredKind)
            {
                case "yawn":
                    { string[] s = { "C01", "C03", "C04", "C05", "C04", "C06", "C07", "C00" }; n = s[Math.Min(s.Length - 1, (int)(t / 680))]; break; }
                case "up":
                    { string[] s = { "C08", "C09", "C08", "C10", "C10", "C07" }; n = s[Math.Min(s.Length - 1, (int)(t / 800))]; break; }
                case "rest":
                    { string[] s = { "C00", "C02", "C02", "C07", "C02" }; n = s[Math.Min(s.Length - 1, (int)(t / 1000))]; break; }
                default:
                    { string[] s = { "C07", "C14", "C15", "C14", "C15", "C14", "C15", "C14", "C15" }; n = s[Math.Min(s.Length - 1, (int)(t / 1000))]; break; }
            }
            Show(n, 0, 1, 160);
        }
    }
}
