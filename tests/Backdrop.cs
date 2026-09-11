using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
class Backdrop : Form {
    protected override bool ShowWithoutActivation { get { return true; } }
    string tone;
    public Backdrop(string[] args) {
        Text="To-do test background";
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(int.Parse(args[0])-8,int.Parse(args[1])-8,int.Parse(args[2])+16,int.Parse(args[3])+16);
        tone = args.Length > 4 ? args[4] : "dark"; DoubleBuffered = true;
        var timer = new Timer { Interval = 45000 }; timer.Tick += delegate { Close(); }; timer.Start();
    }
    protected override void OnPaint(PaintEventArgs e) {
        if(tone=="pattern") {
            e.Graphics.Clear(Color.FromArgb(166,200,213));
            for(int x=0;x<ClientSize.Width;x+=24)using(var stripe=new SolidBrush((x/24)%2==0?Color.FromArgb(45,91,114):Color.FromArgb(231,239,243)))e.Graphics.FillRectangle(stripe,x,0,24,ClientSize.Height);
            using(var font=new Font("Segoe UI",27,FontStyle.Bold))e.Graphics.DrawString("BACKGROUND DETAIL",font,Brushes.White,60,ClientSize.Height/2);
            return;
        }
        Color[] colors = tone == "light" ? new[] { Color.FromArgb(238,232,221), Color.FromArgb(172,203,218), Color.FromArgb(248,240,228), Color.FromArgb(207,195,218) } : new[] { Color.FromArgb(17,24,42), Color.FromArgb(37,81,105), Color.FromArgb(58,44,79), Color.FromArgb(29,34,52) };
        int step = ClientSize.Width / 3;
        for (int i=0;i<3;i++) using(var b = new LinearGradientBrush(new Rectangle(i*step,0,step+1,ClientSize.Height),colors[i],colors[i+1],20)) e.Graphics.FillRectangle(b,i*step,0,step+1,ClientSize.Height);
        using (var p = new Pen(Color.FromArgb(60,255,255,255),2)) for(int x=70;x<ClientSize.Width;x+=140) e.Graphics.DrawEllipse(p,x,40,270,270);
    }
    [STAThread] static void Main(string[] args) { Application.Run(new Backdrop(args)); }
}
