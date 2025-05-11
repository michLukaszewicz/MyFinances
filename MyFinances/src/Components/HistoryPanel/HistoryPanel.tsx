import type { Transaction } from "../../Models/Transaction";
import transactionHistory from "../../TestData/TransactionHistory.json";
import Box from "../Box/Box";
import "./HistoryPanel.css";
import ListRow from "./ListRow/ListRow";

const history: Transaction[] = transactionHistory.map((item) => ({
  ...item,
  date: new Date(item.date),
  type: item.amount > 0 ? "income" : "expense",
}));

const HistoryPanel = () => {
  return (
    <Box>
      <div className="flex justify-between mb-3">
        <h1 className="font-bold text-2xl">History Panel</h1>
        <a href="#" className="text-gray-500">View All &gt;</a>
      </div>
      <div className="flex flex-col justify-between">
        {history.map((transaction) => (
          <ListRow key={transaction.id} transaction={transaction} />
        ))}
      </div>
    </Box>
  );
};

export default HistoryPanel;
