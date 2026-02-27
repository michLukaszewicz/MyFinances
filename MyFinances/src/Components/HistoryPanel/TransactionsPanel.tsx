import type { Transaction } from "../../Models/Transaction";
import AddTransaction from "../Transactions/AddTransaction";
import type { ClientTransaction } from "../../Models/Dtos/TransactionClientDto";
import TransactionHistory from "../Transactions/TransactionsHistory";

interface Props {
  transactionHistory: ClientTransaction[];
  onAddTransaction: (transaction: Transaction) => void;
  onDeleteTransaction: (id: number) => void;
}

const TransactionsPanel = ({ transactionHistory, onAddTransaction, onDeleteTransaction }: Props) => {
  return (
    <>
      <AddTransaction onAddTransaction={onAddTransaction} />
      <TransactionHistory transactionHistory={transactionHistory} onDeleteTransaction={onDeleteTransaction} />
    </>
  );
};

export default TransactionsPanel;
