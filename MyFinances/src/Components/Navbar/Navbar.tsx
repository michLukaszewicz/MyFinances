import "./Navbar.css";
import logo from "../../assets/logo.png";
import { GiHamburgerMenu } from "react-icons/gi";
import { useState } from "react";
import { RiCloseLargeFill } from "react-icons/ri";
import { NavLink } from "react-router-dom";
import { isAuthenticated, logout } from "../../Services/ApiServices/AuthService";

const links = [
  { path: "/", label: "Home" },
  { path: "/summary", label: "Summary" },
  { path: "/finances", label: "Finances" },
  { path: "/accounts", label: "Accounts" },
];

const Navbar = () => {
  const [isOpen, setIsOpen] = useState(false);

  const toggleMenu = () => {
    setIsOpen(!isOpen);
  };

  return (
    <header className="border-b-2 w-full text-black border-b-blue-600 bg-white drop-shadow-2xl flex justify-between items-center py-3 px-6 top-1">
      <NavLink to="/">
        <img src={logo} alt="logo" className="w-32 h-8 max-md:absolute cursor-pointer top-3" />
      </NavLink>

      <ul
        className={`menu flex justify-center gap-7 font-semibold text-gray-700 mx-auto flex-grow ml-10
        max-md:flex-col max-md:mt-8 max-md:gap-10 max-md:items-center max-md:text-lg max-md:mb-2
        max-md:transition-all max-md:duration-500 max-md:overflow-hidden
        ${isOpen ? "max-md:max-h-96 max-md:opacity-100 max-md:mt-12" : "max-md:max-h-0 max-md:opacity-0"}`}>
        {links.map(({ path, label }) => (
          <li key={path} className="btn">
            <NavLink to={path} className={({ isActive }) => (isActive ? "active" : "")}>
              {label}
            </NavLink>
          </li>
        ))}
        {isAuthenticated() ? (
          <li className="btn md:ml-auto">
            <button onClick={logout} className="text-gray-700 hover:text-blue-600">
              Logout
            </button>
          </li>
        ) : (
          <>
            <li className="btn md:ml-auto">
              <NavLink to="/login" className={({ isActive }) => (isActive ? "active" : "")}>
                Login
              </NavLink>
            </li>
            <li className="btn">
              <NavLink to="/register" className={({ isActive }) => (isActive ? "active" : "")}>
                Register
              </NavLink>
            </li>
          </>
        )}
      </ul>

      <div className="md:hidden absolute right-5 top-5 items-center hover:text-blue-600 cursor-pointer">
        {isOpen ? <RiCloseLargeFill onClick={toggleMenu} /> : <GiHamburgerMenu onClick={toggleMenu} />}
      </div>
    </header>
  );
};

export default Navbar;
