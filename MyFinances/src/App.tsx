import "./App.css";
import BalanceChart from "./Components/BalanceChart/BalanceChart";
import HistoryPanel from "./Components/HistoryPanel/HistoryPanel";
import Navbar from "./Components/Navbar/Navbar";
import PageContent from "./Components/PageContent/PageContent";
import type { Transaction } from "./Models/Transaction";
import transactionHistory from "./TestData/TransactionHistory.json";

const history: Transaction[] = transactionHistory
  .map((item) => ({
    ...item,
    date: new Date(item.date),
  }))
  .sort((a, b) => b.date.getTime() - a.date.getTime());

function App() {
  return (
    <div className="w-full h-full">
      <Navbar />
      <PageContent>
        <BalanceChart history={history} />
        <HistoryPanel history={history} />
      </PageContent>
    </div>
  );
}

export default App;
