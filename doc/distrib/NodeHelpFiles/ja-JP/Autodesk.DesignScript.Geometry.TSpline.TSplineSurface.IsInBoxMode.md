## 詳細
ボックス モードとスムーズ モードは、T スプライン サーフェスを表示する 2 つの方法です。スムーズ モードは、T スプライン サーフェスの実際の形状であり、モデルの外観と寸法をプレビューする場合に便利です。一方、ボックス モードは、サーフェスの構造に対する洞察を示して理解が深まるようにします。大きなジオメトリや複雑なジオメトリをより迅速にプレビューできるオプションでもあります。ボックス モードとスムーズ モードは、最初の T スプライン サーフェスの作成時およびそれ以降に、`TSplineSurface.EnableSmoothMode` などのノードを使用してコントロールできます。

T スプラインが無効になった場合、そのプレビューは自動的にボックス モードに切り替わります。サーフェスが無効になったかどうかを識別する別の方法として、ノード `TSplineSurface.IsInBoxMode` を使用できます。

次の例では、`smoothMode` 入力を true に設定して T スプライン平面サーフェスを作成します。面のうち 2 つが削除され、サーフェスが無効になります。サーフェスのプレビューがボックス モードに切り替わりますが、プレビューだけでは区別できません。サーフェスがボックス モードであることを確認するには、ノード `TSplineSurface.IsInBoxMode` を使用します。
___
## サンプル ファイル

![TSplineSurface.IsInBoxMode](./Autodesk.DesignScript.Geometry.TSpline.TSplineSurface.IsInBoxMode_img.jpg)
