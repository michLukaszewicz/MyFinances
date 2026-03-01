import { useEffect, useState } from "react";
import type { ClientTransaction } from "../../Models/Dtos/TransactionClientDto";
import type { Transaction } from "../../Models/Transaction";
import Box from "../Box/Box";
import { EditTransaction } from "../HistoryPanel/ListRow/EditTransaction";
import ListRow from "../HistoryPanel/ListRow/ListRow";

interface Props {
  transactionHistory: ClientTransaction[];
  onDeleteTransaction: (id: number) => void;
  onEditTransaction: (transaction: Transaction) => void;
}

const TransactionHistory = ({ transactionHistory, onDeleteTransaction, onEditTransaction }: Props) => {
  const [transactionEdit, setTransactionEdit] = useState(transactionHistory.map((transaction) => ({ ...transaction, isEditing: false })));

  useEffect(() => {
    setTransactionEdit(transactionHistory.map((transaction) => ({ ...transaction, isEditing: false })));
  }, [transactionHistory]);

  const onCancelEdit = (id: number) => setTransactionEdit(transactionEdit.map((t) => (t.id === id ? { ...t, isEditing: false } : t)));

  const editTransaction = (transaction: Transaction) => {
    onCancelEdit(transaction.id);
    onEditTransaction(transaction);
  };

  return (
    <Box header="History Panel">
      <div className="flex justify-between mb-3">
        <a href="#" className="text-gray-500">
          View All &gt;
        </a>
      </div>
      <div className="flex flex-col justify-between">
        {transactionEdit.map((transaction) =>
          transaction.isEditing ? (
            <>
              <br />
              <EditTransaction key={transaction.clientId} transaction={transaction} onEditTransaction={editTransaction} onEditCancel={onCancelEdit} />
              <br />
            </>
          ) : (
            <ListRow
              key={transaction.clientId}
              transaction={transaction}
              onDeleteTransaction={onDeleteTransaction}
              onEditStart={(id: number): void => setTransactionEdit(transactionEdit.map((t) => (t.id === id ? { ...t, isEditing: true } : t)))}
            />
          ),
        )}
      </div>
    </Box>
  );
};

export default TransactionHistory;
