using AutoMapper;
using MyFinancesAPI.Models.Identity;

namespace MyFinancesAPI.Maps
{
    public class RegisterDtoToUser : Profile
    {
        public RegisterDtoToUser()
        {
            CreateMap<RegisterDto, User>()
                .ForMember(dest => dest.FirstName, opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.Email))
                .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.Email))
                .ReverseMap();
        }
    }
}
