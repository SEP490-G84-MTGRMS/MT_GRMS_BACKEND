using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.PurchaseOrders.Dtos;

namespace MT_GRMS.Application.PurchaseOrders;

public interface IMatrixExcelImportService
{
    Task<MatrixExcelValidationResultDto> PreviewAsync(
        Stream fileStream,
        MatrixExcelPreviewQuery query,
        CancellationToken cancellationToken = default);

    Task<MatrixExcelImportResultDto> ImportAsync(
        ImportMatrixExcelCommand command,
        CancellationToken cancellationToken = default);
}
