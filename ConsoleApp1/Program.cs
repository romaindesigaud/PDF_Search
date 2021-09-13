//Needed packages:
//Install-Package Microsoft.SharePointOnline.CSOM -Version 16.1.20324.12000
//Install-Package Newtonsoft.Json
//Install-Package itext7 -Version 7.1.12


//Read file from sharepoint
//Upload file to SP and add meta-data
//Get $ sign from amount
//Add ‘N/A’ value when really no value found


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using iText.Kernel.Colors;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Filter;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
//using iText.Layout.Element;
using Microsoft.SharePoint.Client;
using Newtonsoft.Json;


namespace PDF_Search
{
    class Program
    {
        public class Search
        { 
            public string FieldName { get; set; }
            public string SearchMode { get; set; }
            //SearchModes options
            //1. Absolute: returns any text that is under the coordinates provided
            //2. Locate: 
	           // - Search* for a textbox that is exactly the provided "SearchLabel", 
	           // - then finds the textbox directly on the right of it 
	           // - and return its text
            //3. RightOf: 
	           // - Search* for a textbox containing the provided "SearchLabel" and 
	           // - return the text whithin that textbox that follows it immediatly, 
	           // - stopping at the next empty space(if any)
            // * Extact search, case sensitive

            public string SearchLabel { get; set; }
            public float OffSet { get; set; }
            public float[] coordinates { get; set; }
            
            public string result { get; set; }

            public string ToAlias { get; set; }

            public string ForceValue { get; set; }

        }

        public class TextBox
        {

            public TextBox(float theX, float theY, float theX2, float theY2, string theText) {
                this.x = theX;
                this.y = theY;
                this.x2 = theX2;
                this.y2 = theY2;
                this.text = theText;
            }
            public float x { get; set; }
            public float y { get; set; }
            public float x2 { get; set; }
            public float y2 { get; set; }
            public string text { get; set; }

        }

        public class SearchConfig
        {
            public string TemplateName { get; set; }
            public string LocationFrom { get; set; }
            public string ListFromGUID { get; set; }

            public string ListToGUID { get; set; }

            public string LocationTo { get; set; }
            public string SearchFrom { get; set; }
            public string[] Disregard { get; set; }

            public string[,] Outputs { get; set; }
            public Search[] Searches { get; set; }
            
        }

        static void testCoord(PdfDocument theDoc, float[] myCoordinates)
        {
            //PdfDocument pdfDoc = new PdfDocument(new PdfReader("C:/temp/WorkingDir/test.pdf"), new PdfWriter("C:/temp/WorkingDir/test_2.pdf"));
            // add content
            Rectangle rect = new Rectangle(myCoordinates[0], myCoordinates[1], myCoordinates[2], myCoordinates[3]);

            //PdfAnnotation ann = new PdfTextAnnotation(new Rectangle(400, 795, 0, 0))
            //.SetTitle(new PdfString("iText"))
            //.SetContents("Please, fill out the form.");
            //.setOpen(true);
            //pdfDoc.GetFirstPage().AddAnnotation(ann);

            /*
            PdfDocument pdfDoc = docEvent.getDocument();
        PdfPage page = docEvent.getPage();
        PdfCanvas canvas = new PdfCanvas(page.getLastContentStream(), page.getResources(), pdfDoc);
        canvas.setFillColor(new DeviceCmyk(1, 0, 0, 0))
                .rectangle(new Rectangle(20, 10, 10, 820))
                .fillStroke();*/




            PdfPage page = theDoc.GetPage(1);
            PdfCanvas pdfCanvas = new PdfCanvas(page.GetLastContentStream(),page.GetResources(), theDoc);
            pdfCanvas.SetFillColor(DeviceCmyk.MAGENTA);
            pdfCanvas.Rectangle(rect);
            pdfCanvas.FillStroke();


            //pdfCanvas.Rectangle(rect);
            //pdfCanvas.Stroke();
            //iText.Layout.Canvas canvas = new iText.Layout.Canvas(pdfCanvas, rect);
            //PdfFont font = PdfFontFactory.CreateFont(StandardFonts.TIMES_ROMAN);
            //PdfFont bold = PdfFontFactory.CreateFont(StandardFonts.TIMES_BOLD);
            //Text title = new Text("The Strange Case of Dr. Jekyll and Mr. Hyde").SetFont(bold);
            //Text author = new Text("Robert Louis Stevenson").SetFont(font);
            //Paragraph p = new Paragraph().Add(title).Add(" by ").Add(author);
            //canvas.Add(p);
            //theDoc.Close();

        }

        //Helper class that stores our rectangle and text
        public class RectAndText
        {
            public Rectangle Rect;
            public String Text;
            public RectAndText(Rectangle rect, String text)
            {
                this.Rect = rect;
                this.Text = text;
            }
        }

        protected class RightOfFieldTextExtractionStrategy : LocationTextExtractionStrategy
        {
            public string searchText = "";
            public string foundText = "";

            public override void EventOccurred(IEventData data, EventType type)
            {
                if (type.Equals(EventType.RENDER_TEXT))
                {
                    TextRenderInfo renderInfo = (TextRenderInfo)data;
                    if (renderInfo.GetText().Contains(searchText))
                    {
                        //We found something, let's clean it real quick
                        Console.WriteLine("Searching for: "+ searchText);
                        int takefrom = renderInfo.GetText().Trim().IndexOf(searchText) + searchText.Length;
                        foundText = renderInfo.GetText().Substring(takefrom);
                        Console.WriteLine("First found: "+ foundText);
                        if (!foundText.Trim().IndexOf("  ").Equals(-1))
                        {
                            Console.WriteLine("empty space index: " + foundText.Trim().IndexOf(" "));
                            // We have an empty space trailing with more text after - let's remove it
                            foundText = foundText.Trim().Substring(0, foundText.Trim().IndexOf(" "));
                            Console.WriteLine("Second found: " + foundText);
                            //foundText = foundText.Split(" ",4,)[0];
                        }
                    }
                }
            }
        }

        protected class RightFieldFinderTextExtractionStrategy : LocationTextExtractionStrategy
        {

            public RectAndText searchFrom;
            public RectAndText bestMatch = new RectAndText(new Rectangle(0,0,0,0),"No best match found");
            public float offSet = 0;
            public string[] toDisregard;
            public override void EventOccurred(IEventData data, EventType type)
            {
                
                if (type.Equals(EventType.RENDER_TEXT))
                {
                    TextRenderInfo renderInfo = (TextRenderInfo)data;
                    //Console.WriteLine("Y searched :" + searchFrom.Rect.GetY());
                    //Console.WriteLine("Current y (field value: "+ renderInfo.GetText() +" :" + renderInfo.GetDescentLine().GetStartPoint().Get(Vector.I2));

                    float lowerBound = searchFrom.Rect.GetY() - 3; // ToDo: Make it into the OffSet parameter
                    float upperBound = searchFrom.Rect.GetY() + 3;
                    
                    if (renderInfo.GetDescentLine().GetStartPoint().Get(Vector.I2) >= lowerBound && renderInfo.GetDescentLine().GetStartPoint().Get(Vector.I2) <= upperBound)
                    {
                        //// we found something whithin the same line (using offset) 
                        //Console.WriteLine("We fond someting on same line:" + renderInfo.GetText());
                        
                        if (searchFrom.Rect.GetX() < renderInfo.GetDescentLine().GetStartPoint().Get(Vector.I1))
                        {
                            //// we found something same line, more to the right
                            //Console.WriteLine("We found someting on same line and on the right:" + renderInfo.GetText());
                            //// need to check if it is not a string to be discarded (: for instance)
                            if ((toDisregard != null) && (Array.IndexOf(toDisregard,renderInfo.GetText())>-1))
                            {
                                //Console.WriteLine("Disregardedor empty toDisregard");
                            }
                            else
                            {
                                //Console.WriteLine("We have a winner: "+ renderInfo.GetText());
                                bestMatch = new RectAndText(new Rectangle(0, 0, 0, 0), renderInfo.GetText());
                            }
                        }
                    }
                }
            }
        }

        protected class analyzeTextExtractionStrategy : LocationTextExtractionStrategy
        {

            public List<RectAndText> myPoints = new List<RectAndText>();

            public override void EventOccurred(IEventData data, EventType type)
            {
                //Console.WriteLine(type.ToString());
                if (type.Equals(EventType.RENDER_TEXT))
                {
                    // you can first check the type of the event
                    if (!type.Equals(EventType.RENDER_TEXT))
                        return;

                    // now it is safe to cast
                    TextRenderInfo renderInfo = (TextRenderInfo)data;
                    Vector bottomLeft = renderInfo.GetDescentLine().GetStartPoint();
                    Vector topRight = renderInfo.GetAscentLine().GetEndPoint();

                    Rectangle myRect = new Rectangle(
                            bottomLeft.Get(Vector.I1),
                            bottomLeft.Get(Vector.I2),
                            topRight.Get(Vector.I1),
                            topRight.Get(Vector.I2)
                        );


                    myPoints.Add(new RectAndText(myRect, renderInfo.GetText()));

                    //Usefull for debug
                    //Console.WriteLine(">>" + renderInfo.GetText() + 
                    //    " (y=" + renderInfo.GetDescentLine().GetStartPoint().Get(Vector.I2) + 
                    //    ", x=" + renderInfo.GetDescentLine().GetStartPoint().Get(Vector.I1) +
                    //    ", y=" + renderInfo.GetDescentLine().GetEndPoint().Get(Vector.I2) +
                    //    ", x'=" + renderInfo.GetDescentLine().GetEndPoint().Get(Vector.I1) +
                    //    ")");
                    //Console.WriteLine(renderInfo.GetDescentLine().GetEndPoint().Get(Vector.I1));
                }
                //Console.WriteLine(textBoxes.Count);
            }

            //public List<TextBox> GetTextBoxes() {
            //    //return textBoxes;
            
            //}
        }

        protected class BoxFinderTextExtractionStrategy : LocationTextExtractionStrategy
        // Could be removed ??
        {
            //Hold each coordinate
            public List<RectAndText> myPoints = new List<RectAndText>();

            public string SearchLabel;

            public override void EventOccurred(IEventData data, EventType type)
            {
                if (type.Equals(EventType.RENDER_TEXT))
                {
                    // you can first check the type of the event
                    if (!type.Equals(EventType.RENDER_TEXT))
                        return;

                    // now it is safe to cast
                    TextRenderInfo renderInfo = (TextRenderInfo)data;
                    Vector bottomLeft = renderInfo.GetDescentLine().GetStartPoint();
                    Vector topRight = renderInfo.GetAscentLine().GetEndPoint();

                    Rectangle myRect = new Rectangle(
                            bottomLeft.Get(Vector.I1),
                            bottomLeft.Get(Vector.I2),
                            topRight.Get(Vector.I1),
                            topRight.Get(Vector.I2)
                        );

                    //Usefull for debug
                    //Console.WriteLine(">" + renderInfo.GetText() + " (y=" + renderInfo.GetDescentLine().GetStartPoint().Get(Vector.I2) + ", x=" + renderInfo.GetDescentLine().GetStartPoint().Get(Vector.I1)+")");

                    //Console.WriteLine("Boxfinder: " + SearchLabel);

                    if (renderInfo.GetText().Trim().Equals(SearchLabel))
                    {
                        //Console.WriteLine(">>>>>>> Found invoice total at position (x, y):" + myRect.GetX() + ", " + myRect.GetY());
                        myPoints.Add(new RectAndText(myRect, renderInfo.GetText()));
                    }

                }

            }
        }

        static void Main(string[] args)
        {

            
            

            // First, checking if we received parameters
            if (args.Length == 0)
            {
                Console.WriteLine("No parameter, cannot do anything (try -h option ?)");
            }

            string filePath = "";

            //Getting the arguments
            switch (args[0])
            {
                case "-h":
                    Console.WriteLine("Usage:");
                    Console.WriteLine("-v : Print this usage summay");
                    Console.WriteLine("-analyse <file path/name>: analyze the file, showing all PDF elements with text and coordinates");
                    Console.WriteLine("-search <file path/name>: load the JSON config file, and execute its content");
                    break;

                case "-analyse":
                    try
                    {
                        filePath = args[1];
                    }
                    catch (Exception)
                    {
                        throw;
                    }

                    Console.WriteLine("Starting analysis");

                    PdfDocument pdfDoc2 = new PdfDocument(new PdfReader(filePath));
                    Console.WriteLine("New PDF doc created");
                    analyzeTextExtractionStrategy textAnalysis = new analyzeTextExtractionStrategy();
                    //textExtraction2.searchText = theSearch.SearchLabel;
                    PdfTextExtractor.GetTextFromPage(pdfDoc2.GetPage(1), textAnalysis);

                    Console.WriteLine("How many textboxes found: " + textAnalysis.myPoints.Count);
                    Console.WriteLine("-------------------------------" );
                    foreach (RectAndText textbox in textAnalysis.myPoints)
                    {
                        Console.WriteLine(textbox.Text + "(" + textbox.Rect.GetX() + ", " + textbox.Rect.GetY()+")");
                    }

                    break;
                case "-search":
                    try
                    {
                        filePath = args[1];
                    }
                    catch (Exception)
                    {
                        throw;
                    }

                    using (StreamReader r = new StreamReader(filePath)) // This needs to be made dynamic
                    {
                        string json = r.ReadToEnd();
                
                        SearchConfig currSearch = JsonConvert.DeserializeObject<SearchConfig>(json);
                        // Todo: need to check here if arguments are correct

                        //Check if we are loading from Sharepoint or from a local drive
                        if (currSearch.LocationFrom.StartsWith("https://ixmetals.sharepoint.com/"))
                        {
                            Console.WriteLine("we are searching from Sharepoint");
                            try
                            {

                                var password = "SamNoe_$$";
                                var securePassword = new SecureString();
                                foreach (char c in password)
                                {
                                    securePassword.AppendChar(c);
                                }

                                ClientContext ctx2 = new ClientContext(currSearch.LocationFrom);

                                ctx2.Load(ctx2.Web);
                                ctx2.Credentials = new SharePointOnlineCredentials("romain.desigaud@ixmetals.com", securePassword);
                                List list = ctx2.Web.Lists.GetById(new Guid(currSearch.ListFromGUID));  // ) Title("/sites/ITTeam/Assets");
                                //https://ixmetals.sharepoint.com/sites/ITTeam/_layouts/ListEnableTargeting.aspx?List={e07a9c14-753c-4342-be26-61935bb73664}
                                //Guid should contain 32 digits with 4 dashes (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)
                                ctx2.Load(list);
                                ctx2.ExecuteQuery();

                                FileCollection files = list.RootFolder.Files; 
                                //ctx2.Web.GetFolderByServerRelativeUrl("/sites/ITTeam/Assets").Files;
                                ctx2.Load(files);
                                ctx2.ExecuteQuery();

                                Console.WriteLine("Found files:" + files.Count());

                                 Console.WriteLine(Environment.CurrentDirectory);
                                //Console.WriteLine(System.Reflection.Assembly.GetExecutingAssembly().CodeBase);

                                if (!Directory.Exists(Environment.CurrentDirectory+"\\Temp"))
                                {
                                    //Console.WriteLine("Dir does not exist - creating");
                                    Directory.CreateDirectory(Environment.CurrentDirectory + "\\Temp");
                                }
                                


                                foreach (Microsoft.SharePoint.Client.File file in files)
                                {
                                    FileInformation fileInfo = Microsoft.SharePoint.Client.File.OpenBinaryDirect(ctx2, file.ServerRelativeUrl);
                                    ctx2.ExecuteQuery();

                                    var tempFilePath = Environment.CurrentDirectory + "\\Temp\\" + file.Name;
                                    using (var fileStream = new System.IO.FileStream(tempFilePath, System.IO.FileMode.Create))
                                    {
                                        fileInfo.Stream.CopyTo(fileStream);
                                    }
                                }

                                // We force the LocationFrom to the local folder (not super clean)
                                currSearch.LocationFrom = Environment.CurrentDirectory + "\\Temp";
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Error: " + e.Message);
                                //throw;
                            }
                        }


                        if (Directory.Exists(currSearch.LocationFrom))
                        {
                            // getting the files to look into (PDFs only)
                            string[] fileEntries = Directory.GetFiles(currSearch.LocationFrom, "*.pdf");


                            // Preparing storage for the results
                            SearchConfig[] results = new SearchConfig[fileEntries.Length];
                            int i = 0;

                            // Debug: 
                            // To see the rectangle painted on a test PDF, uncomment below and change the index in Searches array
                            // testCoord(null, currSearch.Searches[10].coordinates);

                            // looping through the files
                            foreach (var file in fileEntries)
                            {
                                Console.WriteLine("=======FileBegin=======");
                                Console.WriteLine(file);
                                currSearch.TemplateName = file; // storing the fileName on the currSearch
                                Search[] mySearches = currSearch.Searches;

                                // flipping through all searches/fields
                                foreach (var theSearch in mySearches)
                                {
                                    ////Console.WriteLine("Searching for field:" + theSearch.FieldName + " on: " + theSearch.coordinates[0] + ", " + theSearch.coordinates[1] + ", " + theSearch.coordinates[2] + ", " + theSearch.coordinates[3]);
                                    PdfDocument pdfDoc = new PdfDocument(new PdfReader(file));

                                    if (currSearch.SearchFrom.Equals("Top"))
                                    {
                                        //  Here we invert the y coordinate if coordinate are coming from Top left corner. Else we assume its from bottom left corner.
                                        theSearch.coordinates[1] = (int)Math.Round(pdfDoc.GetPage(1).GetPageSize().GetWidth()) - theSearch.coordinates[1];
                                    }

                                    theSearch.result = goSearch(pdfDoc, theSearch, currSearch.Disregard);
                                    ////Console.WriteLine("Found " + theSearch.FieldName + ": " + theSearch.result);
                                    ////Console.WriteLine("---NextField---");
                                    //Todo: parse the outcome into expected type

                                } // Finished flipping through searches
                                // Saving the result
                                results[i] = currSearch;
                                i =i+1;
                                // reinitialise the currSearch object
                                currSearch = JsonConvert.DeserializeObject<SearchConfig>(json);
                            } // Finished flipping through files

                            // Looping through the desired exports
                            for (int j = 0; j < currSearch.Outputs.GetLength(0); j++)
                            {

                                switch (currSearch.Outputs[j,0])
                                {
                                    case "CSV":
                                        
                                        Console.WriteLine("we want to push CSV: " + currSearch.Outputs[j, 1]);
                                        CSVExport(results, currSearch.Outputs[j, 1]);
                                        break;

                                    case "SharePoint":
                                        Console.WriteLine("we want to push to SharePoint: " + currSearch.Outputs[j, 1]);
                                        SharePointExport(results);
                                        
                                        break;
                                    default:
                                        break;
                                }
                            }
                        }
                        else {
                            Console.WriteLine("directoy does not exists");
                        } // TodO need to throw an exception here
                    }

                    Console.WriteLine("Finished, bye");

                    break;
                default: // we did not understand the parameters, exiting
                    Console.WriteLine("Incorrect or missing parameter, cannot do anything (try -h option ?)");
                    break;
            }

            while (true)
            {
                ConsoleKeyInfo toto = Console.ReadKey();
                if (toto.Equals("k")) { break; };
                Console.WriteLine(toto.Key);
            }

        }


        private static void CSVExport(SearchConfig[] searchResults, string destFile)
        {
            // Building a CSV output, should go somewhere else
            StringBuilder csv = new StringBuilder();

            Console.WriteLine("++++++++++++++++++++++++++++++++++++++++");
            Console.WriteLine("Here are the results:" + searchResults.Length);
            foreach (var field in searchResults[0].Searches)
            {
                csv.Append(field.FieldName + "|");
                Console.Write(field.FieldName + " // ");
            }
            csv.AppendLine();
            Console.WriteLine("");
            Console.WriteLine("--------------------------------------------------------------------");
            foreach (var result in searchResults)
            {
                //Console.WriteLine("File " + result.TemplateName + ":");
                foreach (var field in result.Searches)
                {
                    csv.Append(field.result + "|");
                    Console.Write(field.result + " // ");
                }
                //Console.WriteLine("-----EndOfFile----");
                csv.AppendLine();
                Console.WriteLine("");
            }

            using (StreamWriter outputFile = new StreamWriter(destFile))
            {
                outputFile.WriteLine(csv);
            }
        }

        private static void SharePointExport(SearchConfig[] searchResults) {

            try
            {
                var password = "SamNoe_$$";
                var securePassword = new SecureString();
                foreach (char c in password)
                {
                    securePassword.AppendChar(c);
                }

                string siteUrl = "https://ixmetals.sharepoint.com/sites/ITTeam";
                ClientContext context = new ClientContext(siteUrl);
                context.Credentials = new SharePointOnlineCredentials("romain.desigaud@ixmetals.com", securePassword);

                // The folder we will push to   !!!!  TODO: Make dynamic, this is dirt  !!!!
                var targetFolder = context.Web.GetFolderByServerRelativeUrl("https://ixmetals.sharepoint.com/sites/ITTeam/TestPDF_Dest/");

                foreach (var result in searchResults)
                {
                    Console.WriteLine(result.TemplateName); 
                    // loop through all files and their results

                    // 1. Upload the file
                    var fileCreationInfo = new FileCreationInformation
                    {
                        Content = System.IO.File.ReadAllBytes(result.TemplateName),
                        Overwrite = true,
                        Url = System.IO.Path.GetFileName(result.TemplateName)
                    };

                    var uploadFile = targetFolder.Files.Add(fileCreationInfo);
                    context.Load(uploadFile);
                    context.ExecuteQuery();

                    // 2. Loop through the meta-data and update them
                    foreach (var field in result.Searches)
                    {
                        uploadFile.ListItemAllFields[field.ToAlias] = field.result;
                        uploadFile.ListItemAllFields.Update();
                        context.ExecuteQuery();
                    }

                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error SP3:" + e.Message);
            }


        }

        static string goSearch(PdfDocument theDoc, Search theSearch, String[] toDiscard = null)
        {
            string result = "";

            switch (theSearch.SearchMode)
            {
                case "Absolute":
                    Rectangle rect = new Rectangle(theSearch.coordinates[0], theSearch.coordinates[1], theSearch.coordinates[2], theSearch.coordinates[3]);
                    myFilter theFilter = new myFilter(rect);
                    FilteredEventListener listener = new FilteredEventListener();
                    // Create a text extraction renderer
                    LocationTextExtractionStrategy extractionStrategy = listener
                        .AttachEventListener(new LocationTextExtractionStrategy(), theFilter);
                    
                    new PdfCanvasProcessor(listener).ProcessPageContent(theDoc.GetFirstPage());
                    // Get the resultant text after applying the custom filter
                    result = extractionStrategy.GetResultantText();

                    testCoord(theDoc, theSearch.coordinates);

                    break;

                case "RightOf":
                    // Here we need to open a new text location strategy, find the search term, remove the search term and take the leftover right portion of it UNTIL NEXT EMPTY SPACE
                    RightOfFieldTextExtractionStrategy textExtraction2 = new RightOfFieldTextExtractionStrategy();
                    textExtraction2.searchText = theSearch.SearchLabel;
                    PdfTextExtractor.GetTextFromPage(theDoc.GetPage(1), textExtraction2);
                    result = textExtraction2.foundText; 
                    break;

                case "Force":
                    result = theSearch.ForceValue;
                    break;

                case "Locate":
                    // here we need to find the label and its coordinates: 

                    var textExtraction = new BoxFinderTextExtractionStrategy();
                    //Initializing the search label
                    textExtraction.SearchLabel = theSearch.SearchLabel;

                    string ex2 = PdfTextExtractor.GetTextFromPage(theDoc.GetPage(1), textExtraction);

                    if (textExtraction.myPoints.Count() > 0)
                    {
                        // we found something !
                        //then find right-most text and grab it

                        var rightFinderExtraction = new RightFieldFinderTextExtractionStrategy();
                        rightFinderExtraction.searchFrom = textExtraction.myPoints[0];
                        rightFinderExtraction.offSet = theSearch.OffSet;
                        rightFinderExtraction.toDisregard = toDiscard;
                        ex2 = PdfTextExtractor.GetTextFromPage(theDoc.GetPage(1), rightFinderExtraction);
                        result = rightFinderExtraction.bestMatch.Text.Trim();
                    }
                    else
                    {
                        Console.WriteLine("Searching right, did not find (should adjust the offset ?)");
                        // We did not find anything, we should do something here //  TODO
                    }
                    break;
                default:
                    // here we should raise ann exception: unknown search type
                    Console.WriteLine("ERROR : Unknown search type");
                    break;
            }
            return result;
        }



        protected class myFilter : TextRegionEventFilter
        {
            public myFilter(Rectangle filterRect)
                : base(filterRect)
            {
            }
        }
    }
}
