## Подробности
`Mesh.ByVerticesIndices` принимает список `Points`, представляющий вершины (`vertices`) треугольников сети, а также список `indices`, представляющий способ сшивания сети для создания новой сети. Входной параметр `vertices` должен представлять собой неструктурированный список уникальных вершин сети, а входной параметр `indices` — неструктурированный список целых чисел. Каждый набор из трех целых чисел обозначает треугольник в сети. Целые числа определяют индекс вершины в списке вершин. Входные индексы должны считаться от нуля, при этом первая точка списка вершин будет иметь индекс 0.

В приведенном ниже примере узел `Mesh.ByVerticesIndices` создает сеть с помощью списков из девяти вершин (`vertices`) и 36 индексов (`indices`), определяющих комбинацию вершин для каждого из 12 треугольников сети.

## Файл примера

![Example](./Autodesk.DesignScript.Geometry.Mesh.ByVerticesAndIndices_img.jpg)
