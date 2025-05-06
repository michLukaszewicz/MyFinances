import "./Navbar.css";
import logo from "../assets/logo.png";

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
      <ul className="hidden md:flex gap-4 text-md font-semibold text-gray-700">
        <li className="">
          <a>Home</a>
        </li>
        <li className="">
          <a>Summary</a>
        </li>
        <li className="">
          <a>Finances</a>
        </li>
        <li className="">
          <a>Accounts</a>
        </li>
      </ul>
    </header>
  );
};

export default Navbar;
