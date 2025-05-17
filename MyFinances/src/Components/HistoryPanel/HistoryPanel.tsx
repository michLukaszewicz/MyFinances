import type { Transaction } from "../../Models/Transaction";
import Box from "../Box/Box";
import ListRow from "./ListRow/ListRow";

interface Props {
  history: Transaction[];
}

const HistoryPanel = ({history}: Props) => {
  return (
    <Box header="History Panel">
      <div className="flex justify-between mb-3">
        <a href="#" className="text-gray-500">
          View All &gt;
        </a>
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
