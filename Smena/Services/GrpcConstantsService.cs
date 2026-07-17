using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Host.Grpc.Services.Constants;
using Host.Services.Operations;

namespace Host.Services;

public class GrpcConstantsService
    : Host.Grpc.Services.Constants.GrpcConstantsService.GrpcConstantsServiceBase
{
    public override Task<ShiftConstantsResponse> GetShiftConstants(Empty request, ServerCallContext context)
    {
        return Task.FromResult(new ShiftConstantsResponse
        {
            InitialCashRegister = ShiftRules.InitialCashRegister,
            MaxEmployeesPerShift = ShiftRules.MaxEmployeesPerShift,
            MaxHoursPerShift = ShiftRules.MaxHoursPerShift,
            MaxAmountDigits = ShiftRules.MaxAmountDigits,
            MaxHoursDigits = ShiftRules.MaxHoursDigits
        });
    }
}
