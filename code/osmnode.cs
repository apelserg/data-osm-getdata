// ============================
// Обработка OSM файла - NODE (для публикации)
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

namespace osm
{
    class Program
    {
        static string dirIn = @".\in\"; // где лежат файлы (*.bz2)
        static string dirOut = @".\out\"; // куда сохранять результат (если директории нет - создаётся автоматически)

        static string fileLog = "osmnode.log"; // журнал

        static int cntFileTotal = 0; // счётчик обработанных файлов
        static long cntNodeTotal = 0; // счётчик общего количества NODE
        static long cntWayTotal = 0; // счётчик общего количества WAY
        static long cntRelationTotal = 0; // счётчик общего количества RELATION

        static long cntNodeDupl = 0; // счётчик дупликатов NODE

        static long minNode = 1000000000; // минимальный NODE (для статистики)
        static long maxNode = 0; // максимальный NODE (для статистики)
        static long minWay = 1000000000; // минимальный WAY (статистика)
        static long maxWay = 0; // максимальный WAY (статистика)
        static long minRelation = 1000000000; // минимальный RELATION (статистика)
        static long maxRelation = 0; // максимальный RELATION (статистика)

        static int sizeNodeBuffer = 1000000; // после обработки скольких NODE показывать консоль вывода

        // Для контроля дубликатов NODE
        //
        static bool useIndexedCheck = true; // использовать/не использовать индексный массив
                                            // если не использовать (false), то при обработке более одного *.bz2 файла, на границах соседних областей могут появляться дубликаты
                                            // если использовать (true), то потребуется дополнительно ~ 4 GB RAM
        static long nodeIdxSize = (2L * 1024 * 1024 * 1024 - 57 - 1);
        static byte[] nodeIdx1;
        static byte[] nodeIdx2;

        // Country
        //
        static string fileOutNodeCsvCountry = "osmnode-country.csv";
        static string fileOutNodeGeojsonCountry = "osmnode-country.geojson";

        static StringBuilder sbCsvCountry = new StringBuilder();
        static StringBuilder sbGeojsonCountry = new StringBuilder();

        static int cntNodeCountry = 0; // счётчик NODE COUNTRY
        static int cntFileBufferCountry = 0; // счётчик буфера COUNTRY

        // City
        //
        static string fileOutNodeCsvCity = "osmnode-city.csv";
        static string fileOutNodeGeojsonCity = "osmnode-city.geojson";

        static StringBuilder sbCsvCity = new StringBuilder();
        static StringBuilder sbGeojsonCity = new StringBuilder();

        static int cntNodeCity = 0; // счётчик NODE CITY
        static int cntFileBufferCity = 0; // счётчик буфера CITY

        static int numPopulationCityFiltr = 1000000; // фильтр для атрибута Population (показывать города >= numPopulationCityFiltr)

        // Volcano
        //
        static string fileOutNodeCsvVolcano = "osmnode-volcano.csv";
        static string fileOutNodeGeojsonVolcano = "osmnode-volcano.geojson";

        static StringBuilder sbCsvVolcano = new StringBuilder();
        static StringBuilder sbGeojsonVolcano = new StringBuilder();

        static int cntNodeVolcano = 0; // счётчик NODE VOLCANO
        static int cntFileBufferVolcano = 0; // счётчик буфера VOLCANO


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

                if (!Directory.Exists(dirOut))
                    Directory.CreateDirectory(dirOut);

                // пересоздать выходные файлы
                //
                OSM_RecreateFile(fileLog); // пересоздать журнал
                OSM_RecreateFile(fileOutNodeCsvCountry); // пересоздать итоговый файл
                OSM_RecreateFile(fileOutNodeGeojsonCountry);
                OSM_RecreateFile(fileOutNodeCsvCity);
                OSM_RecreateFile(fileOutNodeGeojsonCity);
                OSM_RecreateFile(fileOutNodeCsvVolcano);
                OSM_RecreateFile(fileOutNodeGeojsonVolcano);

                // включить контроль дубликатов
                //
                if (useIndexedCheck)
                {
                    nodeIdx1 = new byte[nodeIdxSize + 1];
                    nodeIdx2 = new byte[nodeIdxSize + 1];
                }

                // Старт
                //
                OSM_WriteLog(fileLog, "===========================");
                OSM_WriteLog(fileLog, String.Format("== Start (check dupls: {0})", useIndexedCheck));
                OSM_WriteLog(fileLog, String.Format("== Find: Country, City ( population >= {0} ), Volcano", numPopulationCityFiltr));
                OSM_WriteLog(fileLog, "===========================");

                OSM_WriteFile(fileOutNodeGeojsonCountry, geojsonHeader); // заголовок GEOJSON
                OSM_WriteFile(fileOutNodeGeojsonCity, geojsonHeader);
                OSM_WriteFile(fileOutNodeGeojsonVolcano, geojsonHeader);

                foreach (string fileFullName in Directory.GetFiles(dirIn, "*.osm.bz2"))
                {
                    OSM_WriteLog(fileLog, String.Format("Start File: {0}", fileFullName));

                    FileInfo fileInfo = new FileInfo(fileFullName);
                    using (FileStream fileStream = fileInfo.OpenRead())
                    {
                        using (Stream unzipStream = new ICSharpCode.SharpZipLib.BZip2.BZip2InputStream(fileStream))
                        {
                            XmlReader xmlReader = XmlReader.Create(unzipStream);

                            while (xmlReader.Read())
                            {
                                if (xmlReader.Name == "node")
                                    OSM_ProcessNode(xmlReader.ReadOuterXml());
                                else if (xmlReader.Name == "way")
                                    OSM_ProcessWay(xmlReader.ReadOuterXml());
                                else if (xmlReader.Name == "relation")
                                    OSM_ProcessRelation(xmlReader.ReadOuterXml());
                            }
                        }
                    }

                    OSM_WriteResultToFiles();  // Записать данные

                    cntFileTotal++;
                    OSM_WriteLog(fileLog, String.Format("End File: {0}", fileFullName));
                    OSM_WriteLog(fileLog, "==");
                }

                OSM_WriteFile(fileOutNodeGeojsonCountry, geojsonFooter); // завершить GEOJSON
                OSM_WriteFile(fileOutNodeGeojsonCity, geojsonFooter);
                OSM_WriteFile(fileOutNodeGeojsonVolcano, geojsonFooter);

                if (useIndexedCheck)
                {
                    OSM_WriteLog(fileLog, "Count dupls");

                    cntNodeDupl = OSM_NodeIdxDuplCount();
                }
            }
            catch (Exception ex)
            {
                OSM_WriteLog(fileLog, "ERROR: " + ex.Message);
            }
            finally
            {
                OSM_WriteLog(fileLog, "===========================");
                OSM_WriteLog(fileLog, String.Format("== Total Files (*.bz2): {0} ; Total Nodes: {1} ; Total Ways: {2} ; Total Relations: {3}", cntFileTotal, cntNodeTotal, cntWayTotal, cntRelationTotal));
                OSM_WriteLog(fileLog, String.Format("== Country: {0} ; City ( population >= {3} ): {1} ; Volcano: {2}", cntNodeCountry, cntNodeCity, cntNodeVolcano, numPopulationCityFiltr));
                OSM_WriteLog(fileLog, String.Format("== Min id Node: {0} ; Max id Node: {1}", minNode, maxNode));
                OSM_WriteLog(fileLog, String.Format("== Min id Way: {0} ; Max id Way: {1}", minWay, maxWay));
                OSM_WriteLog(fileLog, String.Format("== Min id Relation: {0} ; Max id Relation: {1}", minRelation, maxRelation));

                if (useIndexedCheck)
                    OSM_WriteLog(fileLog, String.Format("== Dupls: {0}", cntNodeDupl));

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

            bool addCountry = false;
            bool addCity = false;
            bool addVolcano = false;

            long nodeId = Int64.Parse(xmlDoc.DocumentElement.Attributes["id"].Value);  // ["id"] = [0]

            // Статистика
            //
            cntNodeTotal++;
            if (cntNodeTotal % sizeNodeBuffer == 0)
                OSM_WriteLog(fileLog, String.Format("Processed Nodes: {0}", cntNodeTotal));

            if (nodeId < minNode) minNode = nodeId;
            else if (nodeId > maxNode) maxNode = nodeId;

            // не обрабатывать дубликаты (все встреченные)
            //
            //if (useIndexedCheck)
            //{
            //    if (OSM_NodeIdxAdd(nodeId) > 1)
            //        return;
            //}

            foreach (XmlNode nodeTag in xmlDoc.DocumentElement.ChildNodes)
            {
                if (nodeTag.Name == "tag")
                {
                    if (nodeTag.Attributes["k"].Value == "place" && nodeTag.Attributes["v"].Value == "country") // ["k"] = [0], ["v"] = [1]
                    {
                        addCountry = true;
                        break;
                    }
                    else if (nodeTag.Attributes["k"].Value == "place" && nodeTag.Attributes["v"].Value == "city")
                    {
                        addCity = true;
                        break;
                    }
                    else if (nodeTag.Attributes["k"].Value == "natural" && nodeTag.Attributes["v"].Value == "volcano")
                    {
                        addVolcano = true;
                        break;
                    }
                }
            }

            if (addCountry || addCity || addVolcano)
            {
                // не обрабатывать дубликаты (только для поисковых значений)
                //
                if (useIndexedCheck)
                {
                    if(OSM_NodeIdxAdd(nodeId) > 1)
                        return;
                }

                string strAttrName = "No data";
                string strAttrNameEn = strAttrName;
                string strAttrNameRu = strAttrName;
                string strAttrPopulation = strAttrName;
                string strAttrEle = strAttrName;
                string strAttrType = strAttrName;
                string strAttrStatus = strAttrName;
                string strAttrs = "\"Attrs\":\"No\"";

                string geojsonFeatureBegin = Environment.NewLine + "{" + Environment.NewLine + "\"type\":\"Feature\"," + Environment.NewLine + "\"geometry\":";
                string geojsonFeatureEnd = Environment.NewLine + "},";

                string geojsonPointBegin = "{\"type\":\"Point\",\"coordinates\":";
                string geojsonPointEnd = "},";

                string geojsonPropBegin = Environment.NewLine + "\"properties\":{";
                string geojsonPropEnd = "}";

                int numAttr = 0;
                bool isAttr = false;

                string strLat = xmlDoc.DocumentElement.Attributes["lat"].Value; // ["lat"] = [1]
                string strLon = xmlDoc.DocumentElement.Attributes["lon"].Value; // ["lon"] = [2]

                if (strLat[strLat.Length - 1] == '.')
                {
                    strLat += "0";
                    OSM_WriteLog(fileLog, String.Format("WARNING: NODE ID [{0}] LAT [{1}] changed to [{2}]", nodeId, xmlDoc.DocumentElement.Attributes["lat"].Value, strLat));
                }

                if (strLon[strLon.Length - 1] == '.')
                {
                    strLon += "0";
                    OSM_WriteLog(fileLog, String.Format("WARNING: NODE ID [{0}] LON [{1}] changed to [{2}]", nodeId, xmlDoc.DocumentElement.Attributes["lon"].Value, strLon));
                }

                foreach (XmlNode nodeTag in xmlDoc.DocumentElement.ChildNodes)
                {
                    if (nodeTag.Name == "tag") // ["k"] = [0], ["v"] = [1]
                    {
                        // именованные атрибуты
                        //
                        if (nodeTag.Attributes["k"].Value == "name")
                            strAttrName = nodeTag.Attributes["v"].Value.Replace('\"','\'');
                        else if (nodeTag.Attributes["k"].Value == "name:en")
                            strAttrNameEn = nodeTag.Attributes["v"].Value.Replace('\"', '\'');
                        else if (nodeTag.Attributes["k"].Value == "name:ru")
                            strAttrNameRu = nodeTag.Attributes["v"].Value.Replace('\"', '\'');
                        else if (nodeTag.Attributes["k"].Value == "population")
                            strAttrPopulation = nodeTag.Attributes["v"].Value;
                        else if (nodeTag.Attributes["k"].Value == "ele")
                            strAttrEle = nodeTag.Attributes["v"].Value;
                        else if (nodeTag.Attributes["k"].Value == "type")
                            strAttrType = nodeTag.Attributes["v"].Value;
                        else if (nodeTag.Attributes["k"].Value == "status")
                            strAttrStatus = nodeTag.Attributes["v"].Value;

                        // все атрибуты
                        //
                        if (isAttr)
                        {
                            strAttrs += ",";
                        }
                        else
                        {
                            strAttrs = String.Empty;
                            isAttr = true;
                        }

                        strAttrs += String.Format("\"{0}\":\"{1}\"", nodeTag.Attributes["k"].Value, nodeTag.Attributes["v"].Value.Replace('\"', '\''));
                    }
                }

                if (addCountry)
                {
                    strAttrType = "country"; // установить тип

                    if (!Int32.TryParse(strAttrPopulation, out numAttr))  // ошибка = "0"
                        strAttrPopulation = "0";

                    sbCsvCountry.Append(xmlDoc.DocumentElement.Attributes[0].Value).Append("\t")
                        .Append(strLat).Append("\t")
                        .Append(strLon).Append("\t")
                        .Append(strAttrType).Append("\t")
                        .Append(strAttrName).Append("\t")
                        .Append(strAttrNameEn).Append("\t")
                        .Append(strAttrNameRu).Append("\t")
                        .Append(strAttrPopulation).Append("\t")
                        .Append(strAttrs).Append(Environment.NewLine);

                    sbGeojsonCountry.Append(geojsonFeatureBegin)
                        .Append(geojsonPointBegin)
                        .Append(String.Format("[{0},{1}]", strLon, strLat))
                        .Append(geojsonPointEnd)
                        .Append(geojsonPropBegin)
                        .Append(String.Format("\"{0}\":\"{1}\",", "Type", strAttrType))
                        .Append(String.Format("\"{0}\":\"{1}\",", "Name", strAttrName))
                        .Append(String.Format("\"{0}\":\"{1}\",", "Name(en)", strAttrNameEn))
                        .Append(String.Format("\"{0}\":\"{1}\",", "Name(ru)", strAttrNameRu))
                        .Append(String.Format("\"{0}\":{1}", "Population", strAttrPopulation))
                        .Append(geojsonPropEnd)
                        .Append(geojsonFeatureEnd);

                    cntNodeCountry++;
                    cntFileBufferCountry++;
                }
                else if (addCity)
                {
                    strAttrType = "city"; // установить тип

                    if (!Int32.TryParse(strAttrPopulation, out numAttr)) // ошибка = "0"
                        strAttrPopulation = "0";

                    if (numAttr >= numPopulationCityFiltr) //  с населением >= numPopulationCityFiltr
                    {
                        sbCsvCity.Append(xmlDoc.DocumentElement.Attributes[0].Value).Append("\t")
                            .Append(strLat).Append("\t")
                            .Append(strLon).Append("\t")
                            .Append(strAttrType).Append("\t")
                            .Append(strAttrName).Append("\t")
                            .Append(strAttrNameEn).Append("\t")
                            .Append(strAttrNameRu).Append("\t")
                            .Append(strAttrPopulation).Append("\t")
                            .Append(strAttrs).Append(Environment.NewLine);

                        sbGeojsonCity.Append(geojsonFeatureBegin)
                            .Append(geojsonPointBegin)
                            .Append(String.Format("[{0},{1}]", strLon, strLat))
                            .Append(geojsonPointEnd)
                            .Append(geojsonPropBegin)
                            .Append(String.Format("\"{0}\":\"{1}\",", "Type", strAttrType))
                            .Append(String.Format("\"{0}\":\"{1}\",", "Name", strAttrName))
                            .Append(String.Format("\"{0}\":\"{1}\",", "Name(en)", strAttrNameEn))
                            .Append(String.Format("\"{0}\":\"{1}\",", "Name(ru)", strAttrNameRu))
                            .Append(String.Format("\"{0}\":{1}", "Population", strAttrPopulation))
                            .Append(geojsonPropEnd)
                            .Append(geojsonFeatureEnd);

                        cntNodeCity++;
                        cntFileBufferCity++;
                    }
                }
                else if (addVolcano)
                {
                    strAttrType = "volcano";  // установить тип

                    if (!Int32.TryParse(strAttrEle, out numAttr))  // ошибка = "0"
                        strAttrEle = "0";

                    sbCsvVolcano.Append(xmlDoc.DocumentElement.Attributes[0].Value).Append("\t")
                        .Append(strLat).Append("\t")
                        .Append(strLon).Append("\t")
                        .Append(strAttrType).Append("\t")
                        .Append(strAttrName).Append("\t")
                        .Append(strAttrNameEn).Append("\t")
                        .Append(strAttrNameRu).Append("\t")
                        .Append(strAttrEle).Append("\t")
                        .Append(strAttrStatus).Append("\t")
                        .Append(strAttrs).Append(Environment.NewLine);

                    sbGeojsonVolcano.Append(geojsonFeatureBegin)
                        .Append(geojsonPointBegin)
                        .Append(String.Format("[{0},{1}]", strLon, strLat))
                        .Append(geojsonPointEnd)
                        .Append(geojsonPropBegin)
                        .Append(String.Format("\"{0}\":\"{1}\",", "Type", strAttrType))
                        .Append(String.Format("\"{0}\":\"{1}\",", "Name", strAttrName))
                        .Append(String.Format("\"{0}\":\"{1}\",", "Name(en)", strAttrNameEn))
                        .Append(String.Format("\"{0}\":\"{1}\",", "Name(ru)", strAttrNameRu))
                        .Append(String.Format("\"{0}\":{1},", "Ele", strAttrEle))
                        .Append(String.Format("\"{0}\":\"{1}\"", "Status", strAttrStatus))
                        .Append(geojsonPropEnd)
                        .Append(geojsonFeatureEnd);

                    cntNodeVolcano++;
                    cntFileBufferVolcano++;
                }
            }
        }
        //========
        // Обработать WAY (только для сбора статистики)
        //========
        private static void OSM_ProcessWay(string xmlNode)
        {
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xmlNode);

            long wayId = Int64.Parse(xmlDoc.DocumentElement.Attributes["id"].Value); // ["id"] = [0]

            // Статистика
            //
            cntWayTotal++;
            if (cntWayTotal % sizeNodeBuffer == 0)
                OSM_WriteLog(fileLog, String.Format("Processed Ways: {0}", cntWayTotal));

            if (wayId < minWay) minWay = wayId;
            else if (wayId > maxWay) maxWay = wayId;
        }
        //========
        // Обработать RELATION (только для сбора статистики)
        //========
        private static void OSM_ProcessRelation(string xmlNode)
        {
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xmlNode);

            long relationId = Int64.Parse(xmlDoc.DocumentElement.Attributes["id"].Value); // ["id"] = [0]

            // Статистика
            //
            cntRelationTotal++;
            if (cntRelationTotal % sizeNodeBuffer == 0)
                OSM_WriteLog(fileLog, String.Format("Processed Relations: {0}", cntRelationTotal));

            if (relationId < minRelation) minRelation = relationId;
            else if (relationId > maxRelation) maxRelation = relationId;
        }
        //========
        // Запись результата
        //========
        private static void OSM_WriteResultToFiles()
        {
            if (cntFileBufferCountry > 0)
            {
                OSM_WriteFile(fileOutNodeCsvCountry, sbCsvCountry.ToString());
                OSM_WriteFile(fileOutNodeGeojsonCountry, sbGeojsonCountry.ToString());
                sbCsvCountry.Clear();
                sbGeojsonCountry.Clear();

                cntFileBufferCountry = 0;

                OSM_WriteLog(fileLog, String.Format("COUNTRY = Nodes: {0} ; Total Nodes: {1}", cntNodeCountry, cntNodeTotal));
            }

            if (cntFileBufferCity > 0)
            {
                OSM_WriteFile(fileOutNodeCsvCity, sbCsvCity.ToString());
                OSM_WriteFile(fileOutNodeGeojsonCity, sbGeojsonCity.ToString());
                sbCsvCity.Clear();
                sbGeojsonCity.Clear();

                cntFileBufferCity = 0;

                OSM_WriteLog(fileLog, String.Format("CITY = Nodes: {0} ; Total Nodes: {1}", cntNodeCity, cntNodeTotal));
            }

            if (cntFileBufferVolcano > 0)
            {
                OSM_WriteFile(fileOutNodeCsvVolcano, sbCsvVolcano.ToString());
                OSM_WriteFile(fileOutNodeGeojsonVolcano, sbGeojsonVolcano.ToString());
                sbCsvVolcano.Clear();
                sbGeojsonVolcano.Clear();

                cntFileBufferVolcano = 0;

                OSM_WriteLog(fileLog, String.Format("VOLCANO = Nodes: {0} ; Total Nodes: {1}", cntNodeVolcano, cntNodeTotal));
            }
        }
        //========
        // Функции индексного массива NODE
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