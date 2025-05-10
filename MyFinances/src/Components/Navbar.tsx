import "./Navbar.css";
import logo from "../assets/logo.png";
import { GiHamburgerMenu } from "react-icons/gi";

interface Props {}

const Navbar = (props: Props) => {
  return (
    <header
      className="border-b-2 w-full
     text-black border-b-blue-600 bg-white
     drop-shadow-2xl flex justify-between 
     items-center py-3 px-4"
    >
      <a href="/">
        <img src={logo} alt="logo" className="w-32 h-8" />
      </a>
      <ul className="hidden md:flex gap-7 font-semibold text-gray-700 mx-auto">
        <li className="btn">
          <a className="active">Home</a>
        </li>
        <li className="btn">
          <a >Summary</a>
        </li>
        <li className="btn">
          <a>Finances</a>
        </li>
        <li className="btn">
          <a>Accounts</a>
        </li>
      </ul>

      <ul className="hidden md:flex gap-5 font-semibold text-gray-700">
        <li className="btn">
          <a>Login</a>
        </li>
        <li className="btn">
          <a>Register</a>
        </li>
      </ul>
      <div className="md:hidden flex items-center hover:text-blue-600 cursor-pointer">
        <GiHamburgerMenu />
      </div>
    </header>
  );
};

export default Navbar;
