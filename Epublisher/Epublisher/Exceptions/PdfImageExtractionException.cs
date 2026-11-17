using System;
using System.Collections.Generic;
using System.Text;

namespace Epublisher.Exceptions
{
    internal class PdfImageExtractionException : Exception
    {
        public int? PageNumber { get; }
        public string? FilterName { get; }

        public PdfImageExtractionException()
            : base("Failed to extract image bytes from the PDF document.")
        {
        }

        public PdfImageExtractionException(string message)
            : base(message)
        {
        }

        public PdfImageExtractionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public PdfImageExtractionException(string message, Exception innerException, int? pageNumber = null, string? filterName = null)
            : base(message, innerException)
        {
            PageNumber = pageNumber;
            FilterName = filterName;
        }
    }
}
