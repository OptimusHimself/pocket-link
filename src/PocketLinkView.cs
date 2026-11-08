using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

sealed class ResourceSwitch : CheckBox {
    readonly UiTheme theme;bool hover;
    public ResourceSwitch(UiTheme theme){this.theme=theme;AutoSize=false;Size=new Size(52,38);AccessibleRole=AccessibleRole.CheckButton;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Cursor=Cursors.Hand;}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnCheckedChanged(EventArgs e){base.OnCheckedChanged(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float scale=DeviceDpi/96f;float w=40*scale,h=24*scale,x=(Width-w)/2,y=(Height-h)/2;
        using(var path=new GraphicsPath()){path.AddArc(x,y,h,h,90,180);path.AddArc(x+w-h,y,h,h,270,180);path.CloseFigure();using(var fill=new SolidBrush(!Enabled?Color.FromArgb(48,50,54):Checked?theme.Accent:hover?Color.FromArgb(74,78,84):Color.FromArgb(52,56,62)))e.Graphics.FillPath(fill,path);}
        using(var brush=new SolidBrush(!Enabled?theme.Muted:Checked?theme.Background:Color.FromArgb(193,197,203)))e.Graphics.FillEllipse(brush,x+(Checked?19:4)*scale,y+4*scale,16*scale,16*scale);
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,ClientRectangle,theme.Text,theme.Background);
    }
}

sealed class ResourceGlyph : Control {
    readonly int kind;readonly UiTheme theme;
    public ResourceGlyph(int kind,UiTheme theme){this.kind=kind;this.theme=theme;Size=new Size(28,28);SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);TabStop=false;}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TranslateTransform((Width-24)/2f,(Height-24)/2f);using(var p=new Pen(theme.Muted,1.5f)){
        if(kind==0){g.DrawRectangle(p,7,2,11,20);g.DrawLine(p,11,19,14,19);}
        if(kind==1){g.DrawLines(p,new[]{new Point(3,9),new Point(7,9),new Point(12,5),new Point(12,19),new Point(7,15),new Point(3,15),new Point(3,9)});g.DrawArc(p,10,7,9,10,-65,130);g.DrawArc(p,10,3,14,18,-55,110);}
        if(kind==2){g.DrawRectangle(p,2,6,13,12);g.DrawLines(p,new[]{new Point(15,10),new Point(22,6),new Point(22,18),new Point(15,14)});}
        if(kind==3){g.DrawArc(p,8,2,8,13,180,180);g.DrawLine(p,8,8,8,12);g.DrawLine(p,16,8,16,12);g.DrawArc(p,8,8,8,8,0,180);g.DrawArc(p,5,6,14,13,0,180);g.DrawLine(p,5,11,5,12);g.DrawLine(p,19,11,19,12);g.DrawLine(p,12,19,12,23);}
    }}
}

// Layout and rendering only. MainForm binds actions and provides resource state.
sealed class PocketLinkView : UserControl {
    readonly UiTheme theme;
    public readonly ComboBox Devices=new ComboBox(),Facing=new ComboBox(),Orientation=new ComboBox();
    public readonly CheckBox Speaker,Camera,Microphone,Mirror=new CheckBox();
    public readonly Button Reconnect=new Button();
    readonly Label phone=new Label(),connection=new Label(),error=new Label();
    readonly Label[] states={new Label(),new Label(),new Label()};
    public PocketLinkView(UiTheme theme){
        this.theme=theme;BackColor=theme.Background;ForeColor=theme.Text;Font=theme.Body;Dock=DockStyle.Fill;
        Speaker=new ResourceSwitch(theme){AccessibleName="Speaker"};Camera=new ResourceSwitch(theme){AccessibleName="Camera"};Microphone=new ResourceSwitch(theme){AccessibleName="Microphone"};
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(30,22,30,16),ColumnCount=1,RowCount=5};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(int h in new[]{80,86,136,86})root.RowStyles.Add(new RowStyle(SizeType.Absolute,h));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(root);
        var deviceBar=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,Margin=Padding.Empty};deviceBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,40));deviceBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));deviceBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,115));deviceBar.Controls.Add(new ResourceGlyph(0,theme){Anchor=AnchorStyles.Left},0,0);
        var deviceInfo=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(0,6,0,12)};deviceInfo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));deviceInfo.RowStyles.Add(new RowStyle(SizeType.Percent,55));deviceInfo.RowStyles.Add(new RowStyle(SizeType.Percent,45));phone.Text="No phone";phone.Dock=DockStyle.Fill;phone.TextAlign=ContentAlignment.MiddleLeft;Devices.Dock=DockStyle.Fill;StyleChoice(Devices);Devices.Visible=false;deviceInfo.Controls.Add(phone,0,0);deviceInfo.Controls.Add(Devices,0,0);connection.Dock=DockStyle.Fill;connection.Font=theme.Small;connection.ForeColor=theme.Muted;deviceInfo.Controls.Add(connection,0,1);deviceBar.Controls.Add(deviceInfo,1,0);
        StyleButton(Reconnect);Reconnect.Text="Reconnect";Reconnect.Anchor=AnchorStyles.Right;Reconnect.Size=new Size(108,34);deviceBar.Controls.Add(Reconnect,2,0);root.Controls.Add(deviceBar,0,0);
        root.Controls.Add(ResourceRow("Speaker",Speaker,states[0],1,null),0,1);
        Facing.Items.AddRange(new object[]{"Front","Rear"});Orientation.Items.AddRange(new object[]{"Portrait · 270°","Portrait · 90°","Landscape · 0°","Landscape · 180°"});StyleChoice(Facing);StyleChoice(Orientation);
        var options=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,Margin=Padding.Empty,Padding=new Padding(40,0,0,14)};options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,26));options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,49));options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));Facing.Dock=Orientation.Dock=DockStyle.Fill;Facing.Margin=new Padding(0,0,8,0);Orientation.Margin=new Padding(0,0,8,0);Mirror.Appearance=Appearance.Button;Mirror.Text="Mirror";Mirror.TextAlign=ContentAlignment.MiddleCenter;Mirror.Dock=DockStyle.Fill;Mirror.FlatStyle=FlatStyle.Flat;Mirror.FlatAppearance.BorderColor=theme.ControlBorder;Mirror.FlatAppearance.CheckedBackColor=theme.SelectedBackground;Mirror.Margin=Padding.Empty;Mirror.CheckedChanged+=(s,e)=>Mirror.ForeColor=Mirror.Checked?theme.Accent:theme.Text;options.Controls.Add(Facing,0,0);options.Controls.Add(Orientation,1,0);options.Controls.Add(Mirror,2,0);
        root.Controls.Add(ResourceRow("Camera",Camera,states[1],2,options),0,2);root.Controls.Add(ResourceRow("Microphone",Microphone,states[2],3,null),0,3);
        error.Dock=DockStyle.Fill;error.ForeColor=theme.Error;error.Font=theme.Small;error.Padding=new Padding(40,8,0,0);error.Visible=false;error.AutoEllipsis=true;root.Controls.Add(error,0,4);
    }
    Control ResourceRow(string name,CheckBox toggle,Label state,int glyph,Control options){
        var section=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=options==null?1:2,Margin=Padding.Empty};section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));section.RowStyles.Add(new RowStyle(SizeType.Absolute,84));if(options!=null)section.RowStyles.Add(new RowStyle(SizeType.Percent,100));section.Paint+=(s,e)=>{using(var p=new Pen(theme.Line))e.Graphics.DrawLine(p,0,0,section.Width,0);};
        var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,Margin=Padding.Empty,Padding=new Padding(0,1,0,0)};row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,40));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,88));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,52));row.Controls.Add(new ResourceGlyph(glyph,theme){Anchor=AnchorStyles.Left},0,0);row.Controls.Add(new Label{Text=name,Font=theme.Title,AutoSize=false,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,UseCompatibleTextRendering=true},1,0);state.Dock=DockStyle.Fill;state.TextAlign=ContentAlignment.MiddleRight;state.Font=theme.Small;state.ForeColor=theme.Muted;state.Text="Off";row.Controls.Add(state,2,0);toggle.Anchor=AnchorStyles.Right;row.Controls.Add(toggle,3,0);section.Controls.Add(row,0,0);if(options!=null)section.Controls.Add(options,0,1);return section;
    }
    void StyleChoice(ComboBox c){c.DropDownStyle=ComboBoxStyle.DropDownList;c.FlatStyle=FlatStyle.Standard;c.DrawMode=DrawMode.OwnerDrawFixed;c.DrawItem+=(s,e)=>{if(e.Index<0)return;using(var background=new SolidBrush(e.State.HasFlag(DrawItemState.Selected)?theme.ControlHover:theme.ControlBackground))e.Graphics.FillRectangle(background,e.Bounds);TextRenderer.DrawText(e.Graphics,c.GetItemText(c.Items[e.Index]),c.Font,e.Bounds,theme.Text,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);e.DrawFocusRectangle();};c.BackColor=theme.ControlBackground;c.ForeColor=theme.Text;c.AccessibleName=c==Facing?"Camera lens":c==Orientation?"Camera rotation":"USB phone";}
    void StyleButton(Button b){b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=0;b.FlatAppearance.MouseOverBackColor=Color.FromArgb(44,48,53);b.BackColor=theme.Background;b.ForeColor=theme.Text;}
    public void ShowDevice(Device selected,int count,bool scanning,bool reconnecting){phone.Visible=count<=1;Devices.Visible=count>1;phone.Text=selected==null?"No phone":selected.Description;connection.Text=reconnecting?"Reconnecting…":scanning?"Checking USB…":selected==null?"Connect via USB":selected.Ready?"●  USB connected":selected.State=="unauthorized"?"Allow USB debugging on phone":"Phone offline";connection.ForeColor=selected!=null&&selected.Ready&&!scanning&&!reconnecting?theme.Accent:theme.Muted;}
    public void ShowState(PhoneResource key,string status){string text=status=="运行中"?"On":status=="连接中"?"Starting…":status=="停止中"?"Stopping…":status=="切换镜头中"?"Switching…":status=="失败"||status=="停止失败"?"Error":"Off";states[(int)key].Text=text;states[(int)key].ForeColor=text=="On"?theme.Accent:text=="Error"?theme.Error:theme.Muted;}
    public void ShowError(string message){error.Text=message==null?"":"Could not complete the request. See Diagnostics in the tray menu.";error.Visible=message!=null;error.AccessibleDescription=message;}
}
