namespace TVPPdfConverter.Models;

public record InvoiceLine(
    string InvoiceNumber,
    DateTime FechaEmision,
    string Artista,
    string Concepto,
    string Instrumento,
    string FechaDesde,
    string FechaHasta,
    string HoraDesde,
    string HoraHasta,
    int Dias,
    int Horas,
    decimal Unitario,
    decimal Subtotal,
    // Nuevos campos adicionales de la factura
    decimal SubtotalFactura,
    decimal AporteContribucionOS,
    decimal Jubilacion,
    decimal RecursoAdministrativo,
    decimal Tasa,
    decimal Transporte,
    decimal TotalFactura);
