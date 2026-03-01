import { useEffect, useState } from "react";
import type { ClientTransaction } from "../../Models/Dtos/TransactionClientDto";
import type { Transaction } from "../../Models/Transaction";
import Box from "../Box/Box";
import { EditListRow } from "../HistoryPanel/ListRow/EditListRow";
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
              <EditListRow key={transaction.clientId} transaction={transaction} onEditTransaction={onEditTransaction} />
              <br />
            </>
          ) : (
            <ListRow
              key={transaction.clientId}
              transaction={transaction}
              onDeleteTransaction={onDeleteTransaction}
              onEditStart={(id: number): void =>
                setTransactionEdit(transactionEdit.map((transaction) => (transaction.id === id ? { ...transaction, isEditing: true } : transaction)))
              }
            />
          ),
        )}
      </div>
    </Box>
  );
};

export default TransactionHistory;
