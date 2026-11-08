using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

static class UiRenderProbe {
    [STAThread] static void Main(string[] args){
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        using(var theme=new UiTheme()){
            if(!theme.Title.FontFamily.Name.Contains("Orbitron"))throw new Exception("Bundled title font did not load.");
            foreach(float scale in new[]{1f,1.5f,2f})using(var form=new Form())using(var view=new PocketLinkView(theme)){
                form.AutoScaleMode=AutoScaleMode.None;form.ClientSize=new Size((int)(660*scale),(int)(470*scale));form.Controls.Add(view);view.Facing.SelectedIndex=0;view.Orientation.SelectedIndex=0;
                view.ShowDevice(new Device{Description="Android Phone",State="device"},1,false,false);
                form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();view.Scale(new SizeF(scale,scale));form.PerformLayout();view.PerformLayout();Application.DoEvents();
                if(view.Speaker.Checked||view.Camera.Checked||view.Microphone.Checked)throw new Exception("UI must start with resources off.");
                using(var bitmap=new Bitmap(form.ClientSize.Width,form.ClientSize.Height)){view.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(args[0],"pocketlink-ui-"+(int)(scale*100)+".png"));}
            }
        }
        Console.WriteLine("PASS: bundled Orbitron/icon loaded; offscreen UI rendered at 100%, 150%, 200%.");
    }
}
