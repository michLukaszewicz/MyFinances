import "./Navbar.css";
import logo from "../assets/logo.png";
import { GiHamburgerMenu } from "react-icons/gi";
import { useState } from "react";
import { RiCloseLargeFill } from "react-icons/ri";

interface Props {}

const Navbar = (props: Props) => {
  const [isOpen, setIsOpen] = useState(false);

  const toggleMenu = () => { 
    setIsOpen(!isOpen); 
    if (isOpen){
      document.querySelector('.menu')?.classList
    }
  };

  return (
    <header className="border-b-2 w-full text-black border-b-blue-600 bg-white drop-shadow-2xl flex justify-between items-center py-3 px-4 top-1">
      <a href="/">
        <img src={logo} alt="logo" className="w-32 h-8 max-md:absolute top-3" />
      </a>

<ul
  className={`menu flex justify-center gap-7 font-semibold text-gray-700 mx-auto flex-grow
    max-md:flex-col max-md:mt-8 max-md:gap-10 max-md:items-center max-md:text-lg
    max-md:transition-all max-md:duration-500 max-md:overflow-hidden
    ${isOpen ? "max-md:max-h-96 max-md:opacity-100 max-md:mt-12" : "max-md:max-h-0 max-md:opacity-0"}`}
>

        <li className="btn">
          <a className="active">Home</a>
        </li>
        <li className="btn">
          <a>Summary</a>
        </li>
        <li className="btn">
          <a>Finances</a>
        </li>
        <li className="btn">
          <a>Accounts</a>
        </li>
      </ul>

      <ul className="hidden md:flex gap-5 font-semibold text-gray-700 ml-auto">
        <li className="btn">
          <a>Login</a>
        </li>
        <li className="btn">
          <a>Register</a>
        </li>
      </ul>

      <div className="md:hidden absolute right-5 top-5 items-center hover:text-blue-600 cursor-pointer">
        {isOpen ? (
          <RiCloseLargeFill onClick={toggleMenu} />
        ) : (
          <GiHamburgerMenu onClick={toggleMenu} />
        )}
      </div>
    </header>
  );
};

export default Navbar;
