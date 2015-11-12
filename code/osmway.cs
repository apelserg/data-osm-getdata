// ============================
// Обработка OSM файла - WAY (для публикации)
// ============================
// Разработчик: apelserg ; https://github.com/apelserg/
// Лицензия: WTFPL
// ============================
// ICSharpCode.SharpZipLib.dll: http://icsharpcode.github.io/SharpZipLib/
// ============================
using System;
using System.Text;
using System.IO;
using System.Xml;
using System.Collections.Generic;

namespace osm
{
    class Program
    {
        static string dirIn = @".\in\"; // где лежат файлы (*.bz2)
        static string dirOut = @".\out\"; // куда сохранять результат

        static string fileLog = "osmway.log"; // журнал

        static int cntFileTotal = 0; // счётчик обработанных файлов
        static long cntNodeTotal = 0; // счётчик общего количества NODE
        static long cntWayTotal = 0; // счётчик общего количества WAY
        static long cntNodeDupl = 0; // счётчик дупликатов NODE
        static long cntWayDupl = 0; // счётчик дупликатов WAY

        static long minNode = 1000000000; // минимальный NODE (статистика)
        static long maxNode = 0; // максимальный NODE (статистика)
        static long minWay = 1000000000; // минимальный WAY (статистика)
        static long maxWay = 0; // максимальный NODE (статистика)

        static int sizeFileBuffer = 1000; // размер буфера для записи в файл
        static int sizeLogPeriod = 1000000; // после обработки скольких WAY/NODE показывать консоль вывода

        // Для контроля дубликатов NODE и WAY
        //
        static bool useIndexedCheck = true; // использовать/не использовать индексный массив
                                            // если не использовать (false), то при обработке более одного *.bz2 файла, на границах соседних областей могут появляться дубликаты
                                            // если использовать (true), то потребуется дополнительно ~ 4 GB RAM
        static long wayIdxSize = 512 * 1024 * 1024 - 1;
        static byte[] wayIdx;
        static long nodeIdxSize = (2L * 1024 * 1024 * 1024 - 57 - 1);
        static byte[] nodeIdx1;
        static byte[] nodeIdx2;

        // national_park
        //
        static string fileOutWayCsvNpark_WayToNode = "osmway-desert(way-to-node).csv";
        static string fileOutWayCsvNpark_WayAttrs = "osmway-desert(way-attrs).csv";
        static string fileOutWayCsvNpark_NodeAttrs = "osmway-desert(node-attrs).csv";
        static string fileOutWayGeojsonNpark = "osmway-desert.geojson";

        static long cntWayNpark = 0; // счётчик WAY [national_park]
        static long cntNodeNpark = 0; // счётчик NODE [national_park]

        // структуры хранения
        //
        class NodeAttrItem // для node-тегов (здесь только node, которые входят в way и имеют собственные теги)
        {
            public long NodeId = 0;
            public double Lat = 0;
            public double Lon = 0;
            public string Type;
            public string Name;
            public string NameEn;
            public string NameRu;
            public string Attrs;
        }
        class WayAttrItem // для way-тегов
        {
            public long WayId = 0;
            public string Type;
            public string Name;
            public string NameEn;
            public string NameRu;
            public string Attrs;
        }
        class WayToNodeItem // для линий
        {
            public long WayId = 0;
            public long NodeId = 0;
            public double Lat = 0;
            public double Lon = 0;
        }

        static List<NodeAttrItem> nodeAttrList = new List<NodeAttrItem>();
        static List<WayAttrItem> wayAttrList = new List<WayAttrItem>();
        static List<WayToNodeItem> wayToNodeList = new List<WayToNodeItem>();

        static void Main(string[] args)
        {
            try
            {
                string geojsonHeader = "{\"type\":\"FeatureCollection\",\"features\":[";
                string geojsonFooter = Environment.NewLine + "{}]}";

                // создать выходную директорию (если отсутствует)
                //
                OperatingSystem os = Environment.OSVersion;
                PlatformID pid = os.Platform;

                if (pid == PlatformID.Unix || pid == PlatformID.MacOSX) // 0 - Win32S, 1 - Win32Windows, 2 - Win32NT, 3 - WinCE, 4 - Unix, 5 - Xbox, 6 - MacOSX
                {
                    dirIn = dirIn.Replace(@"\", @"/");
                    dirOut = dirOut.Replace(@"\", @"/");
                }

                if (!Directory.Exists(dirOut)) // Создать выходную директорию (если отсутствует)
                    Directory.CreateDirectory(dirOut);

                // пересоздать выходные файлы
                //
                OSM_RecreateFile(fileLog); // пересоздать журнал
                OSM_RecreateFile(fileOutWayCsvNpark_WayToNode); // пересоздать итоговый файл
                OSM_RecreateFile(fileOutWayCsvNpark_WayAttrs);
                OSM_RecreateFile(fileOutWayCsvNpark_NodeAttrs);
                OSM_RecreateFile(fileOutWayGeojsonNpark);

                // Контроль дубликатов
                //
                if (useIndexedCheck)
                {
                    wayIdx = new byte[wayIdxSize + 1];
                    nodeIdx1 = new byte[nodeIdxSize + 1];
                    nodeIdx2 = new byte[nodeIdxSize + 1];
                }

                // Старт
                //
                OSM_WriteLog(fileLog, "===========================");
                OSM_WriteLog(fileLog, String.Format("== Start (check dupls: {0})", useIndexedCheck));
                OSM_WriteLog(fileLog, "== Find: Deserts");
                OSM_WriteLog(fileLog, "===========================");

                OSM_WriteFile(fileOutWayGeojsonNpark, geojsonHeader); // заголовок GEOJSON

                foreach (string fileFullName in Directory.GetFiles(dirIn, "*.osm.bz2"))
                {
                    OSM_WriteLog(fileLog, String.Format("Start File: {0}", fileFullName));

                    FileInfo fileInfo = new FileInfo(fileFullName);

                    for (int numCycle = 0; numCycle < 2; numCycle++) // два прохода по XML (сперва WAY, потом NODE)
                    {
                        using (FileStream fileStream = fileInfo.OpenRead())
                        {
                            using (Stream unzipStream = new ICSharpCode.SharpZipLib.BZip2.BZip2InputStream(fileStream))
                            {
                                XmlReader xmlReader = XmlReader.Create(unzipStream);

                                while (xmlReader.Read())
                                {
                                    if (numCycle == 0 && xmlReader.Name == "way")
                                        OSM_ProcessWay(xmlReader.ReadOuterXml());
                                    else if (numCycle == 1 && xmlReader.Name == "node")
                                        OSM_ProcessNode(xmlReader.ReadOuterXml());
                                }
                            }
                        }
                    }

                    // Записать данные
                    //
                    OSM_WriteLog(fileLog, "Start write CSV and GEOJSON data");

                    OSM_WriteResultToFilesCsv(); 
                    OSM_WriteResultToFilesGeojson();

                    nodeAttrList.Clear(); // Очистить массивы перед обработкой следующего файла
                    wayAttrList.Clear();
                    wayToNodeList.Clear();

                    OSM_WriteLog(fileLog, "End write CSV and GEOJSON data");

                    cntFileTotal++;

                    OSM_WriteLog(fileLog, String.Format("End File: {0}", fileFullName));
                    OSM_WriteLog(fileLog, "==");
                }

                OSM_WriteFile(fileOutWayGeojsonNpark, geojsonFooter); // завершение GEOJSON

                if (useIndexedCheck)
                {
                    OSM_WriteLog(fileLog, "Count dupls");

                    cntNodeDupl = OSM_NodeIdxDuplCount();
                    cntWayDupl = OSM_WayIdxDuplCount();
                }
            }
            catch (Exception ex)
            {
                OSM_WriteLog(fileLog, "ERROR: " + ex.Message);
            }
            finally
            {
                OSM_WriteLog(fileLog, "===========================");
                OSM_WriteLog(fileLog, String.Format("== Total Files (*.bz2): {0} ; Total Nodes: {1} ; Total Ways: {2}", cntFileTotal, cntNodeTotal, cntWayTotal));
                OSM_WriteLog(fileLog, String.Format("== National Parks: Attrs Nodes: {0} ; Attrs Ways: {1} ", cntNodeNpark, cntWayNpark));
                OSM_WriteLog(fileLog, String.Format("== Min Node: {0} ; Max Node: {1}", minNode, maxNode));
                OSM_WriteLog(fileLog, String.Format("== Min Way: {0} ; Max Way: {1}", minWay, maxWay));

                if (useIndexedCheck)
                    OSM_WriteLog(fileLog, String.Format("== Node dupls: {0} ; Way dupls: {1}", cntNodeDupl, cntWayDupl));

                OSM_WriteLog(fileLog, "===========================");
                OSM_WriteLog(fileLog, "== End");
                OSM_WriteLog(fileLog, "===========================");
            }
        }
        //========
        // Обработать NODE
        //========
        private static void OSM_ProcessNode(string xmlNode)
        {
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xmlNode);

            long nodeId = Int64.Parse(xmlDoc.DocumentElement.Attributes["id"].Value); // ["id"] = [0]

            // Статистика
            //
            cntNodeTotal++;

            if (cntNodeTotal % sizeLogPeriod == 0)
                OSM_WriteLog(fileLog, String.Format("Processed Nodes: {0}", cntNodeTotal));

            if (nodeId < minNode) minNode = nodeId;
            else if (nodeId > maxNode) maxNode = nodeId;

            // не обрабатывать дубликаты
            //
            if (useIndexedCheck)
            {
                byte nodeStatus = OSM_NodeIdxGet(nodeId);

                // nodeStatus == 0 (значит НЕ входит в состав WAY)
                //
                if (nodeStatus == 0) // не обрабатывать ненужные значения
                    return;

                // nodeStatus > 1 (значит это дубликат)
                //
                else if (nodeStatus > 1)  // не обрабатывать дубликаты
                {
                    OSM_NodeIdxAdd(nodeId);
                    return;
                }

                // nodeStatus == 1 (значит входит в состав WAY и ещё не был обработан)
                //
                OSM_NodeIdxAdd(nodeId);
            }

            // if (wayToNodeList.Exists(x => x.NodeId == nodeId)) // ускоряет это или нет?
            {
                foreach (WayToNodeItem wayToNodeItem in wayToNodeList)
                {
                    if (wayToNodeItem.NodeId == nodeId)
                    {
                        wayToNodeItem.Lat = Double.Parse(xmlDoc.DocumentElement.Attributes["lat"].Value); // ["lat"] = [1]
                        wayToNodeItem.Lon = Double.Parse(xmlDoc.DocumentElement.Attributes["lon"].Value); // ["lon"] = [2]

                        // если node id встретилось в первый раз - вычислить атрибуты и записать
                        //
                        if (!nodeAttrList.Exists(x => x.NodeId == nodeId))
                        {
                            // собрать все атарибуты NODE (если есть)
                            //
                            string nodeType = "No type";
                            string nodeName = "No name";
                            string nodeNameEn = nodeName;
                            string nodeNameRu = nodeName;
                            string nodeAttrs = "\"Attrs\":\"No\"";

                            bool isAttrs = false;

                            foreach (XmlNode nodeTag in xmlDoc.DocumentElement.ChildNodes)
                            {
                                if (nodeTag.Name == "tag")
                                {
                                    if (isAttrs) nodeAttrs += ",";
                                    else nodeAttrs = String.Empty;

                                    isAttrs = true;

                                    nodeAttrs += String.Format("\"{0}\":\"{1}\"", nodeTag.Attributes["k"].Value, nodeTag.Attributes["v"].Value.Replace('\"', '\''));

                                    if (nodeTag.Attributes["k"].Value == "name")
                                        nodeName = nodeTag.Attributes["v"].Value.Replace('\"', '\'');
                                    else if (nodeTag.Attributes["k"].Value == "name:en")
                                        nodeNameEn = nodeTag.Attributes["v"].Value.Replace('\"', '\'');
                                    else if (nodeTag.Attributes["k"].Value == "name:ru")
                                        nodeNameRu = nodeTag.Attributes["v"].Value.Replace('\"', '\'');
                                }
                            }

                            if (isAttrs)
                            {
                                NodeAttrItem nodeAttrItem = new NodeAttrItem();

                                nodeAttrItem.NodeId = nodeId;
                                nodeAttrItem.Lat = wayToNodeItem.Lat;
                                nodeAttrItem.Lon = wayToNodeItem.Lon;
                                nodeAttrItem.Type = nodeType;
                                nodeAttrItem.Name = nodeName;
                                nodeAttrItem.NameEn = nodeNameEn;
                                nodeAttrItem.NameRu = nodeNameRu;
                                nodeAttrItem.Attrs = nodeAttrs;

                                nodeAttrList.Add(nodeAttrItem);

                                cntNodeNpark++;
                            }
                        }
                    }
                }
            }
        }
        //========
        // Обработать WAY
        //========
        private static void OSM_ProcessWay(string xmlWay)
        {
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xmlWay);

            long wayId = Int64.Parse(xmlDoc.DocumentElement.Attributes["id"].Value); // ["id"] = [0]

            // Статистика
            //
            cntWayTotal++;

            if (cntWayTotal % sizeLogPeriod == 0)
                OSM_WriteLog(fileLog, String.Format("Processed Ways: {0}", cntWayTotal));

            if (wayId < minWay) minWay = wayId;
            else if (wayId > maxWay) maxWay = wayId;

            // не обрабатывать дубликаты (все встреченные)
            //
            //if (useIndexedCheck)
            //{
            //    if (OSM_WayIdxAdd(wayId) > 1) // дубликаты не обрабатывать
            //        return;
            //}

            foreach (XmlNode wayTag in xmlDoc.DocumentElement.ChildNodes)
            {
                if (wayTag.Name == "tag" && wayTag.Attributes["k"].Value == "natural" && wayTag.Attributes["v"].Value == "desert") // ["k"] = [0], ["v"] = [1]
                {
                    // не обрабатывать дубликаты (только для поисковых значений)
                    //
                    if (useIndexedCheck)
                    {
                        if(OSM_WayIdxAdd(wayId) > 1)
                            return;
                    }

                    // добавить WAY
                    //
                    if (!wayToNodeList.Exists(x => x.WayId == wayId))
                    {
                        bool isAttrs = false;

                        WayAttrItem wayAttrItem = new WayAttrItem();

                        wayAttrItem.WayId = wayId;
                        wayAttrItem.Type = wayTag.Attributes["v"].Value; // ["v"] = [1]
                        wayAttrItem.Name = "No name";
                        wayAttrItem.NameEn = wayAttrItem.Name;
                        wayAttrItem.NameRu = wayAttrItem.Name;
                        wayAttrItem.Attrs = "\"Attrs\":\"No\"";

                        foreach (XmlNode wayNd in xmlDoc.DocumentElement.ChildNodes)
                        {
                            if (wayNd.Name == "nd")
                            {
                                long nodeId = Int64.Parse(wayNd.Attributes["ref"].Value); // ["ref"] = [0]

                                if (useIndexedCheck)
                                    OSM_NodeIdxSet(nodeId);

                                WayToNodeItem wayToNodeItem = new WayToNodeItem();

                                wayToNodeItem.WayId = wayId;
                                wayToNodeItem.NodeId = nodeId;

                                wayToNodeList.Add(wayToNodeItem);
                            }
                            else if (wayNd.Name == "tag") // собрать все аттрибуты WAY
                            {
                                if (isAttrs) wayAttrItem.Attrs += ",";
                                else wayAttrItem.Attrs = String.Empty;

                                isAttrs = true;

                                wayAttrItem.Attrs += String.Format("\"{0}\":\"{1}\"", wayNd.Attributes["k"].Value, wayNd.Attributes["v"].Value.Replace('\"', '\''));

                                if (wayNd.Attributes["k"].Value == "name")
                                    wayAttrItem.Name = wayNd.Attributes["v"].Value.Replace('\"', '\'');
                                else if (wayNd.Attributes["k"].Value == "name:en")
                                    wayAttrItem.NameEn = wayNd.Attributes["v"].Value.Replace('\"', '\'');
                                else if (wayNd.Attributes["k"].Value == "name:ru")
                                    wayAttrItem.NameRu = wayNd.Attributes["v"].Value.Replace('\"', '\'');
                            }
                        }

                        if (isAttrs)
                            wayAttrList.Add(wayAttrItem);

                        cntWayNpark++;
                    }
                }
            }
        }
        //========
        // Запись результата в CSV
        //========
        private static void OSM_WriteResultToFilesCsv()
        {
            StringBuilder sbCsv = new StringBuilder();
            int cntToFile = 0;

            // WAY TO NODE
            //
            foreach (WayToNodeItem wayToNodeItem in wayToNodeList)
            {
                sbCsv.Append(wayToNodeItem.WayId.ToString()).Append("\t")
                    .Append(wayToNodeItem.NodeId.ToString()).Append("\t")
                    .Append(wayToNodeItem.Lat.ToString()).Append("\t")
                    .Append(wayToNodeItem.Lon.ToString()).Append(Environment.NewLine);

                // записать буфер в файл
                //
                cntToFile++;
                if (cntToFile == sizeFileBuffer)
                {
                    OSM_WriteFile(fileOutWayCsvNpark_WayToNode, sbCsv.ToString());
                    sbCsv.Clear();
                    cntToFile = 0;
                }
            }
            // записать буфер в файл
            //
            if (cntToFile > 0)
            {
                OSM_WriteFile(fileOutWayCsvNpark_WayToNode, sbCsv.ToString());
                sbCsv.Clear();
                cntToFile = 0;
            }

            // WAY ATTRS
            //
            foreach (WayAttrItem wayAttrItem in wayAttrList)
            {
                sbCsv.Append(wayAttrItem.WayId.ToString()).Append("\t")
                    .Append(wayAttrItem.Type).Append("\t")
                    .Append(wayAttrItem.Name).Append("\t")
                    .Append(wayAttrItem.NameEn).Append("\t")
                    .Append(wayAttrItem.NameRu).Append("\t")
                    .Append(wayAttrItem.Attrs).Append(Environment.NewLine);

                // записать буфер в файл
                //
                cntToFile++;
                if (cntToFile == sizeFileBuffer)
                {
                    OSM_WriteFile(fileOutWayCsvNpark_WayAttrs, sbCsv.ToString());
                    sbCsv.Clear();
                    cntToFile = 0;
                }
            }
            // записать буфер в файл
            //
            if (cntToFile > 0)
            {
                OSM_WriteFile(fileOutWayCsvNpark_WayAttrs, sbCsv.ToString());
                sbCsv.Clear();
                cntToFile = 0;
            }

            // NODE ATTRS
            //
            foreach (NodeAttrItem nodeAttrItem in nodeAttrList)
            {
                sbCsv.Append(nodeAttrItem.NodeId.ToString()).Append("\t")
                    .Append(nodeAttrItem.Lat.ToString()).Append("\t")
                    .Append(nodeAttrItem.Lon.ToString()).Append("\t")
                    .Append(nodeAttrItem.Type).Append("\t")
                    .Append(nodeAttrItem.Name).Append("\t")
                    .Append(nodeAttrItem.NameEn).Append("\t")
                    .Append(nodeAttrItem.NameRu).Append("\t")
                    .Append(nodeAttrItem.Attrs).Append(Environment.NewLine);

                // записать буфер в файл
                //
                cntToFile++;
                if (cntToFile == sizeFileBuffer)
                {
                    OSM_WriteFile(fileOutWayCsvNpark_NodeAttrs, sbCsv.ToString());
                    sbCsv.Clear();
                    cntToFile = 0;
                }
            }
            // записать буфер в файл
            //
            if (cntToFile > 0)
            {
                OSM_WriteFile(fileOutWayCsvNpark_NodeAttrs, sbCsv.ToString());
            }
        }
        //========
        // Запись результата в GEOJSON
        //========
        private static void OSM_WriteResultToFilesGeojson()
        {
            string geojsonFeatureBegin = Environment.NewLine + "{" + Environment.NewLine + "\"type\":\"Feature\"," + Environment.NewLine + "\"geometry\":";
            string geojsonFeatureEnd = Environment.NewLine + "},";

            string geojsonPointBegin = "{\"type\":\"Point\",\"coordinates\":";
            string geojsonPointEnd = "},";

            string geojsonPolygonBegin = "{\"type\":\"Polygon\",\"coordinates\":[[";
            string geojsonPolygonEnd = "]]}";

            string geojsonPropBegin = Environment.NewLine + "\"properties\":{";
            string geojsonPropEnd = "}";

            StringBuilder sbGeojson = new StringBuilder();

            // WAY Attrs (Points)
            //
            int cntToFile = 0;

            foreach (WayAttrItem wayAttrItem in wayAttrList)
            {
                WayToNodeItem wayToNodeItem = wayToNodeList.Find(x => x.WayId == wayAttrItem.WayId);

                if (wayToNodeItem == null)
                    OSM_WriteLog(fileLog, String.Format("WARNING: wayToNodeItem == null (wayAttrItem.WayId == {0})", wayAttrItem.WayId));
                else
                {
                    sbGeojson.Append(geojsonFeatureBegin)
                        .Append(geojsonPointBegin)
                        .Append(String.Format("[{0},{1}]", wayToNodeItem.Lon, wayToNodeItem.Lat))
                        .Append(geojsonPointEnd)
                        .Append(geojsonPropBegin)
                        .Append(String.Format("\"{0}\":\"{1}\",", "Type", wayAttrItem.Type))
                        .Append(String.Format("\"{0}\":\"{1}\",", "Name", wayAttrItem.Name))
                        .Append(String.Format("\"{0}\":\"{1}\",", "Name(en)", wayAttrItem.NameEn))
                        .Append(String.Format("\"{0}\":\"{1}\"", "Name(ru)", wayAttrItem.NameRu))
                        .Append(geojsonPropEnd)
                        .Append(geojsonFeatureEnd);

                    cntToFile++;
                }

                // записать буфер в файл
                //
                if (cntToFile == sizeFileBuffer)
                {
                    OSM_WriteFile(fileOutWayGeojsonNpark, sbGeojson.ToString());
                    sbGeojson.Clear();
                    cntToFile = 0;
                }
            }
            // записать буфер в файл
            //
            if (cntToFile > 0)
            {
                OSM_WriteFile(fileOutWayGeojsonNpark, sbGeojson.ToString());
                sbGeojson.Clear();
                cntToFile = 0;
            }

            // WAY (Polygon)
            //
            long prevId = 0;

            foreach (WayToNodeItem wayToNodeItem in wayToNodeList)
            {
                if (prevId == 0)
                    sbGeojson.Append(geojsonFeatureBegin)
                        .Append(geojsonPolygonBegin);
                else if (prevId == wayToNodeItem.WayId)
                    sbGeojson.Append(",");
                else
                    sbGeojson.Append(geojsonPolygonEnd)
                        .Append(geojsonFeatureEnd)
                        .Append(geojsonFeatureBegin)
                        .Append(geojsonPolygonBegin);

                sbGeojson.Append(String.Format("[{0},{1}]", wayToNodeItem.Lon, wayToNodeItem.Lat));

                prevId = wayToNodeItem.WayId;

                // записать буфер в файл
                //
                cntToFile++;
                if (cntToFile == sizeFileBuffer)
                {
                    OSM_WriteFile(fileOutWayGeojsonNpark, sbGeojson.ToString());
                    sbGeojson.Clear();
                    cntToFile = 0;
                }
            }
            // записать буфер в файл
            //
            if (cntToFile > 0)
            {
                OSM_WriteFile(fileOutWayGeojsonNpark, sbGeojson.ToString());
                sbGeojson.Clear();
            }
            // закрыть последний элемент POLYGON в GEOJSON и записать буфер в файл
            //
            if (prevId != 0)
            {
                sbGeojson.Append(geojsonPolygonEnd)
                    .Append(geojsonFeatureEnd);

                OSM_WriteFile(fileOutWayGeojsonNpark, sbGeojson.ToString());
            }
        }
        //========
        // Функции индексного массива NODE
        //========
        private static void OSM_NodeIdxSet(long nodeId)
        {
            if (nodeId <= nodeIdxSize)
                nodeIdx1[nodeId] = 1;
            else
                nodeIdx2[nodeId - nodeIdxSize] = 1;
        }
        //========
        private static byte OSM_NodeIdxGet(long nodeId)
        {
            if (nodeId <= nodeIdxSize)
                return nodeIdx1[nodeId];

            return nodeIdx2[nodeId - nodeIdxSize];
        }
        //========
        private static byte OSM_NodeIdxAdd(long nodeId)
        {
            if (nodeId <= nodeIdxSize)
                return ++nodeIdx1[nodeId];

            return ++nodeIdx2[nodeId - nodeIdxSize];
        }
        //========
        private static long OSM_NodeIdxDuplCount()
        {
            long numDupl = 0;

            for (long n = 0; n <= nodeIdxSize; n++)
            {
                if (nodeIdx1[n] > 2) numDupl++;
                if (nodeIdx2[n] > 2) numDupl++;
            }
            return numDupl;
        }
        //========
        // Функции индексного массива WAY
        //========
        private static byte OSM_WayIdxAdd(long wayId)
        {
            return ++wayIdx[wayId];
        }
        //========
        private static long OSM_WayIdxDuplCount()
        {
            long numDupl = 0;

            for (long n = 0; n <= wayIdxSize; n++)
                if (wayIdx[n] > 1) numDupl++;

            return numDupl;
        }
        //========
        // Пересоздать файл
        //========
        private static void OSM_RecreateFile(string fileName)
        {
            string fileFullName = dirOut + fileName;

            if (File.Exists(fileFullName))
                File.Delete(fileFullName);

            using (File.Create(fileFullName));
        }
        //========
        // Записать в файл
        //========
        private static void OSM_WriteFile(string fileName, string strX)
        {
            string fileFullName = dirOut + fileName;

            if (File.Exists(fileFullName))
                using (StreamWriter sw = File.AppendText(fileFullName))
                    sw.Write(strX);
            else
                using (StreamWriter sw = File.CreateText(fileFullName))
                    sw.Write(strX);
        }
        //========
        // Записать в журнал
        //========
        private static void OSM_WriteLog(string fileLog, string strLog)
        {
            strLog = "[" + DateTime.Now.ToString(@"hh\:mm\:ss") + "] " + strLog;
            Console.WriteLine(strLog);
            OSM_WriteFile(fileLog, strLog + Environment.NewLine);
        }
    }
}