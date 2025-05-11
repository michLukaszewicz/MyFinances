import "./App.css";
import HistoryPanel from "./Components/HistoryPanel/HistoryPanel";
import Navbar from "./Components/Navbar/Navbar";
import PageContent from "./Components/PageContent/PageContent";

function App() {
  return (
    <div className="w-full h-full">
      <Navbar />
      <PageContent>
        <HistoryPanel />
      </PageContent>
    </div>
  );
}

export default App;
