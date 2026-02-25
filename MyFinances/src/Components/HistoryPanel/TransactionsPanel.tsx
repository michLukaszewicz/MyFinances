import type { Transaction } from "../../Models/Transaction";
import AddTransaction from "../Transactions/AddTransaction";
import type { ClientTransaction } from "../../Models/Dtos/TransactionClientDto";
import TransactionHistory from "../Transactions/TransactionsHistory";

interface Props {
  transactionHistory: ClientTransaction[];
  onAddTransaction: (transaction: Transaction) => void;
}

const TransactionsPanel = ({ transactionHistory, onAddTransaction }: Props) => {
  return (
    <>
      <AddTransaction onAddTransaction={onAddTransaction} />
      <TransactionHistory transactionHistory={transactionHistory} />
    </>
  );
};

export default TransactionsPanel;
