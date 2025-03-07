## 深入資訊
`List.Sublists` 會根據輸入範圍和偏移，從給定清單傳回一系列子清單。範圍決定輸入清單中要放入第一個子清單的元素。對範圍套用偏移後，新範圍決定第二個子清單。此程序會一直重複，依給定偏移增加範圍的起始索引，直到產生空的子清單為止。

在以下範例中，我們先產生一個從 0 到 9 的數字範圍。使用範圍 0 到 5 作為子清單範圍，偏移為 2。在巢狀子清單的輸出中，第一個清單包含索引在範圍 0..5 的元素，第二個清單包含索引在 2..7 的元素。重複後，範圍的結尾超過初始清單長度，後續的子清單就會變短。
___
## 範例檔案

![List.Sublists](./DSCore.List.Sublists_img.jpg)
