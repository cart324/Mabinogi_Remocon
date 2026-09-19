using System;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;
namespace MabiRemote {
public static class UiSizing {
    public static readonly int[] Options={70,80,90,100,110,125};
    public static int Percent=100;
}
public sealed class UiScaleState:IDisposable {
    sealed class Metrics {
        public Control Control;public Rectangle Bounds;public Size Minimum,Maximum;public Padding Margin,Padding;
        public Font Font;public float[] Rows,Columns;public Padding CellPadding;public int RowHeight;public int[] ColumnMin;
        public Point TabPadding;public int SplitDistance;
    }
    readonly List<Metrics> baseline=new List<Metrics>();readonly List<Font> generated=new List<Font>();
    public UiScaleState(Control root){foreach(Control child in root.Controls)Capture(child);}
    void Capture(Control c){
        var m=new Metrics{Control=c,Bounds=c.Bounds,Minimum=c.MinimumSize,Maximum=c.MaximumSize,Margin=c.Margin,Padding=c.Padding,Font=(Font)c.Font.Clone()};
        var table=c as TableLayoutPanel;if(table!=null){m.Rows=table.RowStyles.Cast<RowStyle>().Select(x=>x.SizeType==SizeType.Absolute?x.Height:-1).ToArray();m.Columns=table.ColumnStyles.Cast<ColumnStyle>().Select(x=>x.SizeType==SizeType.Absolute?x.Width:-1).ToArray();}
        var grid=c as DataGridView;if(grid!=null){m.CellPadding=grid.DefaultCellStyle.Padding;m.RowHeight=grid.RowTemplate.Height;m.ColumnMin=grid.Columns.Cast<DataGridViewColumn>().Select(x=>x.MinimumWidth).ToArray();}
        var tab=c as TabControl;if(tab!=null)m.TabPadding=tab.Padding;
        var split=c as SplitContainer;if(split!=null)m.SplitDistance=split.SplitterDistance;
        baseline.Add(m);if(grid==null)foreach(Control child in c.Controls)Capture(child);
    }
    static int Px(int value,float factor){return (int)Math.Round(value*factor);}
    static Size Scaled(Size size,float factor){return new Size(Px(size.Width,factor),Px(size.Height,factor));}
    static Padding Scaled(Padding p,float f){return new Padding(Px(p.Left,f),Px(p.Top,f),Px(p.Right,f),Px(p.Bottom,f));}
    public void Apply(int percent){
        float factor=percent/100F;var previous=generated.ToArray();generated.Clear();
        foreach(var m in baseline)m.Control.SuspendLayout();
        try{foreach(var m in baseline){var c=m.Control;if(c.IsDisposed)continue;
            var font=new Font(m.Font.FontFamily,m.Font.Size*factor,m.Font.Style,m.Font.Unit,m.Font.GdiCharSet);generated.Add(font);c.Font=font;
            c.MinimumSize=Scaled(m.Minimum,factor);c.MaximumSize=Scaled(m.Maximum,factor);c.Margin=Scaled(m.Margin,factor);c.Padding=Scaled(m.Padding,factor);
            if(c.Dock==DockStyle.None)c.Bounds=new Rectangle(Px(m.Bounds.X,factor),Px(m.Bounds.Y,factor),Px(m.Bounds.Width,factor),Px(m.Bounds.Height,factor));
            else if(c.Dock==DockStyle.Top||c.Dock==DockStyle.Bottom)c.Height=Px(m.Bounds.Height,factor);
            else if(c.Dock==DockStyle.Left||c.Dock==DockStyle.Right)c.Width=Px(m.Bounds.Width,factor);
            var table=c as TableLayoutPanel;if(table!=null){for(int i=0;i<m.Rows.Length;i++)if(m.Rows[i]>=0)table.RowStyles[i].Height=m.Rows[i]*factor;for(int i=0;i<m.Columns.Length;i++)if(m.Columns[i]>=0)table.ColumnStyles[i].Width=m.Columns[i]*factor;}
            var grid=c as DataGridView;if(grid!=null){grid.DefaultCellStyle.Font=font;grid.ColumnHeadersDefaultCellStyle.Font=font;grid.DefaultCellStyle.Padding=Scaled(m.CellPadding,factor);grid.RowTemplate.Height=Math.Max(2,Px(m.RowHeight,factor));for(int i=0;i<m.ColumnMin.Length;i++)grid.Columns[i].MinimumWidth=Math.Max(2,Px(m.ColumnMin[i],factor));}
            var tab=c as TabControl;if(tab!=null)tab.Padding=new Point(Px(m.TabPadding.X,factor),Px(m.TabPadding.Y,factor));
        }}finally{for(int i=baseline.Count-1;i>=0;i--)if(!baseline[i].Control.IsDisposed)baseline[i].Control.ResumeLayout(true);}
        foreach(var m in baseline){var split=m.Control as SplitContainer;if(split!=null&&!split.IsDisposed){int length=split.Orientation==Orientation.Horizontal?split.ClientSize.Height:split.ClientSize.Width;int max=length-split.Panel2MinSize-split.SplitterWidth;if(max>=split.Panel1MinSize)split.SplitterDistance=Math.Max(split.Panel1MinSize,Math.Min(max,Px(m.SplitDistance,factor)));}}
        foreach(var font in previous)font.Dispose();
    }
    public void Dispose(){foreach(var m in baseline)m.Font.Dispose();foreach(var f in generated)f.Dispose();generated.Clear();baseline.Clear();}
}
public partial class MainForm {
    UiScaleState displayScale;ComboBox displayScaleCombo;
    void AddDisplayScale(FlowLayoutPanel options){var row=Bar();row.Dock=DockStyle.None;row.Controls.Add(Label("UI 배율 (즉시 적용)",10));displayScaleCombo=Combo(160);foreach(int percent in UiSizing.Options)displayScaleCombo.Items.Add(percent+"%");displayScaleCombo.SelectedItem=cfg.UiScale+"%";
        row.Controls.Add(displayScaleCombo);row.Controls.Add(Label("작게 설정하면 같은 창에 더 많이 표시됩니다.",9));options.Controls.Add(row);
        displayScaleCombo.SelectedIndexChanged+=(s,e)=>{if(displayScaleCombo.SelectedIndex<0)return;cfg.UiScale=UiSizing.Options[displayScaleCombo.SelectedIndex];ApplyDisplayScale(cfg.UiScale);Save();};
    }
    void ApplyDisplayScale(int percent){if(displayScale==null)displayScale=new UiScaleState(this);UiSizing.Percent=percent;SuspendLayout();try{displayScale.Apply(percent);}finally{ResumeLayout(true);}PerformLayout();}
    public object CheckUiScale(string directory){
        System.IO.Directory.CreateDirectory(directory);var size=Size;tabs.SelectedIndex=0;Application.DoEvents();float original=facilityGrid.Font.Size;var root=Controls.OfType<TableLayoutPanel>().First();float cardHeight=root.RowStyles[1].Height;
        ApplyDisplayScale(80);Application.DoEvents();bool compact=facilityGrid.Font.Size<original&&root.RowStyles[1].Height<cardHeight&&Size==size;SaveLivePreview(System.IO.Path.Combine(directory,"scale-80.png"));
        for(int i=0;i<3;i++){ApplyDisplayScale(125);ApplyDisplayScale(70);ApplyDisplayScale(100);}Application.DoEvents();bool stable=Math.Abs(facilityGrid.Font.Size-original)<0.01&&Math.Abs(root.RowStyles[1].Height-cardHeight)<0.01&&Size==size;
        SaveLivePreview(System.IO.Path.Combine(directory,"scale-100.png"));ApplyDisplayScale(80);tabs.SelectedIndex=6;Application.DoEvents();using(var bmp=new Bitmap(Width,Height)){DrawToBitmap(bmp,new Rectangle(0,0,Width,Height));bmp.Save(System.IO.Path.Combine(directory,"settings-80.png"));}
        return J.Obj("passed",compact&&stable,"sameWindowSize",Size==size,"compact",compact,"restoresWithoutDrift",stable);
    }
}
}
