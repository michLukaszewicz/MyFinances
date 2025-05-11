import "./App.css";
import HistoryPanel from "./Components/HistoryPanel/HistoryPanel";
import Navbar from "./Components/Navbar/Navbar";
import PageContent from "./Components/PageContent/PageContent";
import type { Transaction } from "./Models/Transaction";
import transactionHistory from "./TestData/TransactionHistory.json";

const history: Transaction[] = transactionHistory.map((item) => ({
  ...item,
  date: new Date(item.date),
  type: item.amount > 0 ? "income" : "expense",
}));

function App() {
  return (
    <div className="w-full h-full">
      <Navbar />
      <PageContent>
        <HistoryPanel history={history} />
      </PageContent>
    </div>
  );
}

export default App;
