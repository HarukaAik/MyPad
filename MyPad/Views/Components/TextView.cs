﻿using System.Windows.Media;

namespace MyPad.Views.Components
{
    public class TextView : ICSharpCode.AvalonEdit.Rendering.TextView
    {
        public TextView()
        {
            // HACK: 依存関係プロパティ ColumnRulerPen の設定
            // おそらく SearchPanel.MarkerBrush と似たような理由だと思われる。
            this.ColumnRulerPen = new Pen(Brushes.DimGray, 2);
        }
    }
}
