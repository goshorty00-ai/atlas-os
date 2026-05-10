using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AtlasAI.Tools
{
    /// <summary>
    /// Document Service - Creates Word documents (.docx) from chat requests.
    /// Supports letters, CVs, reports, and checklists.
    /// </summary>
    public class DocumentService
    {
        private static readonly string OutputDir = @"D:\AtlasAI\Generated\Docs";
        
        /// <summary>
        /// Document types supported
        /// </summary>
        public enum DocumentType
        {
            Letter,
            CV,
            Report,
            Checklist,
            Generic
        }
        
        /// <summary>
        /// Create a Word document with the specified content
        /// </summary>
        public static DocumentResult CreateDoc(
            string title,
            string body,
            List<string>? sections = null,
            List<string>? bullets = null,
            DocumentType type = DocumentType.Generic)
        {
            try
            {
                // Ensure output directory exists
                Directory.CreateDirectory(OutputDir);
                
                // Generate filename
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var safeTitle = SanitizeFilename(title);
                var filename = $"{safeTitle}_{timestamp}.docx";
                var fullPath = Path.Combine(OutputDir, filename);
                
                Debug.WriteLine($"[DocumentService] Creating {type} document: {filename}");
                
                // Create the document
                using (WordprocessingDocument wordDoc = WordprocessingDocument.Create(fullPath, WordprocessingDocumentType.Document))
                {
                    // Add main document part
                    MainDocumentPart mainPart = wordDoc.AddMainDocumentPart();
                    mainPart.Document = new Document();
                    Body docBody = mainPart.Document.AppendChild(new Body());
                    
                    // Add title
                    AddTitle(docBody, title);
                    
                    // Add date (for letters)
                    if (type == DocumentType.Letter)
                    {
                        AddParagraph(docBody, DateTime.Now.ToString("MMMM dd, yyyy"), alignment: JustificationValues.Right);
                        AddParagraph(docBody, ""); // Blank line
                    }
                    
                    // Add body content
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        // Split body into paragraphs
                        var paragraphs = body.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var para in paragraphs)
                        {
                            AddParagraph(docBody, para.Trim());
                        }
                    }
                    
                    // Add sections if provided
                    if (sections != null && sections.Count > 0)
                    {
                        AddParagraph(docBody, ""); // Blank line
                        foreach (var section in sections)
                        {
                            AddHeading(docBody, section);
                        }
                    }
                    
                    // Add bullets if provided
                    if (bullets != null && bullets.Count > 0)
                    {
                        AddParagraph(docBody, ""); // Blank line
                        foreach (var bullet in bullets)
                        {
                            AddBulletPoint(docBody, bullet);
                        }
                    }
                    
                    // Add signature line (for letters)
                    if (type == DocumentType.Letter)
                    {
                        AddParagraph(docBody, ""); // Blank line
                        AddParagraph(docBody, ""); // Blank line
                        AddParagraph(docBody, "Sincerely,");
                        AddParagraph(docBody, ""); // Blank line
                        AddParagraph(docBody, ""); // Blank line
                        AddParagraph(docBody, "[Your Name]");
                    }
                    
                    mainPart.Document.Save();
                }
                
                Debug.WriteLine($"[DocumentService] Document created successfully: {fullPath}");
                
                return new DocumentResult
                {
                    Success = true,
                    FilePath = fullPath,
                    FileName = filename,
                    Message = $"✅ Document created: {filename}\n📁 Location: {OutputDir}"
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DocumentService] Error creating document: {ex.Message}");
                return new DocumentResult
                {
                    Success = false,
                    Message = $"❌ Failed to create document: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// Create a letter document
        /// </summary>
        public static DocumentResult CreateLetter(string subject, string recipient, string body)
        {
            var fullBody = $"Dear {recipient},\n\n{body}";
            return CreateDoc(subject, fullBody, type: DocumentType.Letter);
        }
        
        /// <summary>
        /// Create a CV/Resume document
        /// </summary>
        public static DocumentResult CreateCV(string name, List<string> sections, string summary)
        {
            return CreateDoc($"CV - {name}", summary, sections: sections, type: DocumentType.CV);
        }
        
        /// <summary>
        /// Create a report document
        /// </summary>
        public static DocumentResult CreateReport(string title, string body, List<string>? sections = null)
        {
            return CreateDoc(title, body, sections: sections, type: DocumentType.Report);
        }
        
        /// <summary>
        /// Create a checklist document
        /// </summary>
        public static DocumentResult CreateChecklist(string title, List<string> items)
        {
            return CreateDoc(title, "", bullets: items, type: DocumentType.Checklist);
        }
        
        /// <summary>
        /// Add a title to the document
        /// </summary>
        private static void AddTitle(Body body, string text)
        {
            Paragraph para = body.AppendChild(new Paragraph());
            Run run = para.AppendChild(new Run());
            run.AppendChild(new Text(text));
            
            // Format as title
            RunProperties runProps = run.InsertAt(new RunProperties(), 0);
            runProps.AppendChild(new Bold());
            runProps.AppendChild(new FontSize { Val = "32" }); // 16pt
            
            ParagraphProperties paraProps = para.InsertAt(new ParagraphProperties(), 0);
            paraProps.AppendChild(new Justification { Val = JustificationValues.Center });
            paraProps.AppendChild(new SpacingBetweenLines { After = "240" }); // Space after
        }
        
        /// <summary>
        /// Add a heading to the document
        /// </summary>
        private static void AddHeading(Body body, string text)
        {
            Paragraph para = body.AppendChild(new Paragraph());
            Run run = para.AppendChild(new Run());
            run.AppendChild(new Text(text));
            
            // Format as heading
            RunProperties runProps = run.InsertAt(new RunProperties(), 0);
            runProps.AppendChild(new Bold());
            runProps.AppendChild(new FontSize { Val = "28" }); // 14pt
            
            ParagraphProperties paraProps = para.InsertAt(new ParagraphProperties(), 0);
            paraProps.AppendChild(new SpacingBetweenLines { Before = "240", After = "120" });
        }
        
        /// <summary>
        /// Add a paragraph to the document
        /// </summary>
        private static void AddParagraph(Body body, string text, JustificationValues? alignment = null)
        {
            Paragraph para = body.AppendChild(new Paragraph());
            Run run = para.AppendChild(new Run());
            run.AppendChild(new Text(text));
            
            if (alignment.HasValue)
            {
                ParagraphProperties paraProps = para.InsertAt(new ParagraphProperties(), 0);
                paraProps.AppendChild(new Justification { Val = alignment.Value });
            }
        }
        
        /// <summary>
        /// Add a bullet point to the document
        /// </summary>
        private static void AddBulletPoint(Body body, string text)
        {
            Paragraph para = body.AppendChild(new Paragraph());
            Run run = para.AppendChild(new Run());
            run.AppendChild(new Text(text));
            
            // Add bullet formatting
            ParagraphProperties paraProps = para.InsertAt(new ParagraphProperties(), 0);
            
            // Create numbering properties for bullets
            NumberingProperties numProps = new NumberingProperties();
            numProps.AppendChild(new NumberingLevelReference { Val = 0 });
            numProps.AppendChild(new NumberingId { Val = 1 });
            paraProps.AppendChild(numProps);
        }
        
        /// <summary>
        /// Sanitize filename to remove invalid characters
        /// </summary>
        private static string SanitizeFilename(string filename)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", filename.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
            
            // Limit length
            if (sanitized.Length > 50)
                sanitized = sanitized.Substring(0, 50);
            
            return sanitized;
        }
    }
    
    /// <summary>
    /// Result of document creation
    /// </summary>
    public class DocumentResult
    {
        public bool Success { get; set; }
        public string? FilePath { get; set; }
        public string? FileName { get; set; }
        public string Message { get; set; } = "";
    }
}
