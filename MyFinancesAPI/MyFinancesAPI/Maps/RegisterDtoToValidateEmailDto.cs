using AutoMapper;
using MyFinancesAPI.Models.Identity;

namespace MyFinancesAPI.Maps
{
    public class RegisterDtoToSendValidationEmailDto : Profile
    {
        public RegisterDtoToSendValidationEmailDto()
        {
            CreateMap<RegisterDto, SendValidationEmailDto>()
                .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.Email))
                .ForMember(dest => dest.FrontendBaseUrl, opt => opt.MapFrom(src => src.FrontendBaseUrl));
        }
    }
}
