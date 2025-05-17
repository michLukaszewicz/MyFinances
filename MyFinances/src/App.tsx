import { Outlet } from "react-router-dom";
import Navbar from "./Components/Navbar/Navbar";

function App() {
  return (
    <div className="w-full h-full">
      <Navbar />
      <Outlet />
    </div>
  );
}

export default App;
