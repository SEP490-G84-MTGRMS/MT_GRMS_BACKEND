using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.SupplierReturns;

/// <summary>
/// Ràng buộc chính sách trả/đổi NCC dùng chung cho phiếu trả và duyệt kiểm kê.
/// </summary>
public static class SupplierReturnPolicyRules
{
    public static readonly string[] DefaultAllowedReturnTypes =
        ["QUALITY_DEFECT", "NEAR_EXPIRY", "EXPIRED", "WRONG_ITEM"];

    public static readonly string[] DefaultAllowedResolutionTypes =
        ["REDUCE_PAYABLE", "EXCHANGE", "REFUND"];

    public static List<string> ParseJsonList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static string NormalizeResolutionType(string? resolutionType)
    {
        if (string.IsNullOrWhiteSpace(resolutionType))
            return "REDUCE_PAYABLE";

        var raw = resolutionType.Trim().ToUpperInvariant();
        return raw switch
        {
            "REPLACE_GOODS" or "EXCHANGE" => "EXCHANGE",
            "DEDUCT_INVOICE" or "REDUCE_PAYABLE" => "REDUCE_PAYABLE",
            "REFUND" => "REFUND",
            "SUPPLIER_CREDIT" => "SUPPLIER_CREDIT",
            "OTHER" => "OTHER",
            _ => resolutionType.Trim().ToUpperInvariant()
        };
    }

    public static bool IsExchangeResolution(string? resolutionType) =>
        !string.IsNullOrWhiteSpace(resolutionType)
        && resolutionType.Contains("EXCHANGE", StringComparison.OrdinalIgnoreCase);

    public static string NormalizeReturnReasonType(string? reasonType, string? freeTextReason = null)
    {
        if (!string.IsNullOrWhiteSpace(reasonType))
        {
            var raw = reasonType.Trim().ToUpperInvariant();
            return raw switch
            {
                "QUALITY_DEFECT" or "DAMAGED" or "DEFECTIVE" => "QUALITY_DEFECT",
                "NEAR_EXPIRY" or "NEAR_EXPIRY_RETURN" => "NEAR_EXPIRY",
                "EXPIRED" or "EXPIRY" => "EXPIRED",
                "WRONG_ITEM" or "WRONG_SPEC" or "WRONG_SPEC_DISCOVERED" => "WRONG_ITEM",
                "RECALL" or "RECALLED" => "RECALL",
                "OTHER" => "OTHER",
                _ => raw
            };
        }

        var text = (freeTextReason ?? string.Empty).Trim().ToLowerInvariant();
        if (text.Contains("hết hạn") || text.Contains("het han") || text.Contains("expired"))
            return "EXPIRED";
        if (text.Contains("cận hạn") || text.Contains("can han") || text.Contains("near"))
            return "NEAR_EXPIRY";
        if (text.Contains("thu hồi") || text.Contains("thu hoi") || text.Contains("recall"))
            return "RECALL";
        if (text.Contains("sai") || text.Contains("wrong"))
            return "WRONG_ITEM";
        if (text.Contains("hư") || text.Contains("hong") || text.Contains("lỗi") || text.Contains("loi") || text.Contains("chất lượng"))
            return "QUALITY_DEFECT";
        return "OTHER";
    }

    /// <summary>
    /// policy == null → coi như mặc định cho phép (NCC chưa cấu hình).
    /// </summary>
    public static void EnsureReturnAllowed(
        SupplierReturnPolicy? policy,
        string resolutionType,
        string returnReasonType,
        DateTime? receiptDateUtc = null,
        bool requireEvidence = false,
        bool hasEvidence = true)
    {
        var allowReturn = policy?.AllowReturn ?? true;
        if (!allowReturn)
            throw new BusinessException("RETURN_NOT_ALLOWED", "NCC không cho phép trả/đổi hàng theo chính sách.");

        var allowedTypes = policy == null
            ? DefaultAllowedReturnTypes.ToList()
            : ParseJsonList(policy.AllowedReturnTypesJson);
        if (allowedTypes.Count == 0)
            allowedTypes = DefaultAllowedReturnTypes.ToList();

        var normalizedReason = NormalizeReturnReasonType(returnReasonType);
        if (!allowedTypes.Any(t => string.Equals(t, normalizedReason, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BusinessException(
                "RETURN_TYPE_NOT_ALLOWED",
                $"Chính sách NCC không cho trả với lý do '{normalizedReason}'.");
        }

        var normalizedResolution = NormalizeResolutionType(resolutionType);
        var allowedResolutions = policy == null
            ? DefaultAllowedResolutionTypes.ToList()
            : ParseJsonList(policy.AllowedResolutionTypesJson);
        if (allowedResolutions.Count == 0)
            allowedResolutions = DefaultAllowedResolutionTypes.ToList();

        if (!allowedResolutions.Any(r =>
                string.Equals(NormalizeResolutionType(r), normalizedResolution, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BusinessException(
                "RESOLUTION_NOT_ALLOWED",
                $"Chính sách NCC không cho hình thức bồi hoàn '{normalizedResolution}'.");
        }

        var maxDays = policy?.MaxReturnDaysAfterReceipt;
        if (maxDays.HasValue && maxDays.Value >= 0 && receiptDateUtc.HasValue)
        {
            var days = (DateTime.UtcNow.Date - receiptDateUtc.Value.ToUniversalTime().Date).TotalDays;
            if (days > maxDays.Value)
            {
                throw new BusinessException(
                    "RETURN_WINDOW_EXPIRED",
                    $"Đã quá {maxDays.Value} ngày kể từ ngày nhận hàng — không còn được trả theo chính sách NCC.");
            }
        }

        var evidenceRequired = requireEvidence || (policy?.RequireEvidenceImage ?? true);
        if (evidenceRequired && !hasEvidence)
        {
            throw new BusinessException(
                "EVIDENCE_REQUIRED",
                "Chính sách NCC yêu cầu ảnh minh chứng khi trả/đổi hàng.");
        }
    }

    public static void EnsureDispositionAllowed(
        SupplierReturnPolicy? policy,
        string dispositionKind,
        DateTime? receiptDateUtc = null,
        bool hasEvidence = true)
    {
        var reasonType = dispositionKind.Trim().ToUpperInvariant() switch
        {
            "DAMAGED" or "QUALITY_DEFECT" => "QUALITY_DEFECT",
            "EXPIRED" => "EXPIRED",
            "NEAR_EXPIRY" => "NEAR_EXPIRY",
            _ => dispositionKind
        };

        // Khi duyệt kiểm kê chưa chọn hình thức bồi hoàn — chỉ kiểm allowReturn + loại lý do + cửa sổ ngày + ảnh.
        var allowReturn = policy?.AllowReturn ?? true;
        if (!allowReturn)
            throw new BusinessException("RETURN_NOT_ALLOWED", "NCC không cho phép trả/đổi hàng theo chính sách.");

        var allowedTypes = policy == null
            ? DefaultAllowedReturnTypes.ToList()
            : ParseJsonList(policy.AllowedReturnTypesJson);
        if (allowedTypes.Count == 0)
            allowedTypes = DefaultAllowedReturnTypes.ToList();

        if (!allowedTypes.Any(t => string.Equals(t, reasonType, StringComparison.OrdinalIgnoreCase)))
        {
            var label = reasonType == "EXPIRED" ? "hàng hết hạn" : reasonType == "QUALITY_DEFECT" ? "hàng hư hỏng" : reasonType;
            throw new BusinessException(
                "RETURN_TYPE_NOT_ALLOWED",
                $"Chính sách NCC không cho đổi/trả {label}.");
        }

        var maxDays = policy?.MaxReturnDaysAfterReceipt;
        if (maxDays.HasValue && maxDays.Value >= 0 && receiptDateUtc.HasValue)
        {
            var days = (DateTime.UtcNow.Date - receiptDateUtc.Value.ToUniversalTime().Date).TotalDays;
            if (days > maxDays.Value)
            {
                throw new BusinessException(
                    "RETURN_WINDOW_EXPIRED",
                    $"Đã quá {maxDays.Value} ngày kể từ ngày nhận hàng — không còn được trả theo chính sách NCC.");
            }
        }

        if ((policy?.RequireEvidenceImage ?? true) && !hasEvidence)
        {
            throw new BusinessException(
                "EVIDENCE_REQUIRED",
                "Chính sách NCC yêu cầu ảnh minh chứng khi chọn đổi/trả NCC.");
        }
    }
}
